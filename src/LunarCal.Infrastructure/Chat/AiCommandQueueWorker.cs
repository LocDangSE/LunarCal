using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-QUEUE-1..9: Background hosted service that processes AI commands from the SQLite queue.
/// Supports 6 lifecycle states, exponential backoff with jitter on errors (up to 5 retries),
/// crash recovery (processing -> pending), idempotency tracking, and 30-day automatic retention cleanup.
/// </summary>
public sealed class AiCommandQueueWorker : BackgroundService
{
    private readonly IAiCommandStore _commandStore;
    private readonly Func<IChatOrchestrator>? _orchestratorFactory;
    private readonly IChatOrchestrator? _orchestrator;
    private readonly INetworkStatusService _networkStatus;
    private readonly IClock _clock;
    private readonly AiQueueWorkerOptions _options;
    private readonly AiQueueRetryPolicy _retryPolicy;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="AiCommandQueueWorker"/> with a singleton orchestrator.
    /// </summary>
    public AiCommandQueueWorker(
        IAiCommandStore commandStore,
        IChatOrchestrator orchestrator,
        INetworkStatusService networkStatus,
        IClock clock,
        AiQueueWorkerOptions? options = null,
        AiQueueRetryPolicy? retryPolicy = null,
        ILogger? logger = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new AiQueueWorkerOptions();
        _retryPolicy = retryPolicy ?? new AiQueueRetryPolicy(_options.BaseRetryDelay);
        _logger = logger ?? Log.ForContext<AiCommandQueueWorker>();

        _networkStatus.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AiCommandQueueWorker"/> with an orchestrator factory.
    /// </summary>
    public AiCommandQueueWorker(
        IAiCommandStore commandStore,
        Func<IChatOrchestrator> orchestratorFactory,
        INetworkStatusService networkStatus,
        IClock clock,
        AiQueueWorkerOptions? options = null,
        AiQueueRetryPolicy? retryPolicy = null,
        ILogger? logger = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _orchestratorFactory = orchestratorFactory ?? throw new ArgumentNullException(nameof(orchestratorFactory));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new AiQueueWorkerOptions();
        _retryPolicy = retryPolicy ?? new AiQueueRetryPolicy(_options.BaseRetryDelay);
        _logger = logger ?? Log.ForContext<AiCommandQueueWorker>();

        _networkStatus.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AiCommandQueueWorker"/> for DI when an orchestrator is optional.
    /// </summary>
    public AiCommandQueueWorker(
        IAiCommandStore commandStore,
        INetworkStatusService networkStatus,
        IClock clock,
        AiQueueWorkerOptions? options = null,
        AiQueueRetryPolicy? retryPolicy = null,
        ILogger? logger = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new AiQueueWorkerOptions();
        _retryPolicy = retryPolicy ?? new AiQueueRetryPolicy(_options.BaseRetryDelay);
        _logger = logger ?? Log.ForContext<AiCommandQueueWorker>();

        _networkStatus.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    /// <summary>
    /// Signals the background worker to immediately check the queue (e.g. after enqueuing a new command).
    /// </summary>
    public void Trigger()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signaled
            }
        }
    }

    /// <summary>
    /// Performs startup initialization: crash recovery (FR-QUEUE-8) and retention cleanup (FR-QUEUE-9).
    /// </summary>
    public async Task InitializeStartupAsync(CancellationToken cancellationToken = default)
    {
        // 1. Crash recovery: recover commands left in 'processing' by abnormal shutdown (FR-QUEUE-8)
        var recovered = await _commandStore.RecoverProcessingCommandsAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (recovered > 0)
        {
            _logger.Information("Recovered {Count} commands from previous session crash to pending", recovered);
        }

        // 2. Retention cleanup: delete completed/cancelled commands older than 30 days (FR-QUEUE-9)
        var cutoff = _clock.UtcNow - _options.RetentionPeriod;
        var cleaned = await _commandStore.CleanupOldCommandsAsync(cutoff, cancellationToken).ConfigureAwait(false);
        if (cleaned > 0)
        {
            _logger.Information("Cleaned up {Count} expired commands older than {Days} days", cleaned, _options.RetentionPeriod.TotalDays);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("AiCommandQueueWorker starting");

        try
        {
            await InitializeStartupAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed during startup initialization");
        }

        // 3. Worker loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_networkStatus.IsOnline)
                {
                    // FR-QUEUE-3: Offline banner active; commands wait in pending until connection returns
                    await WaitForSignalOrDelayAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var processedCount = await ProcessPendingBatchAsync(stoppingToken).ConfigureAwait(false);
                if (processedCount == 0)
                {
                    await WaitForSignalOrDelayAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled error in AiCommandQueueWorker loop");
                await WaitForSignalOrDelayAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.Information("AiCommandQueueWorker stopped");
    }

    /// <summary>
    /// Processes a single batch of pending commands ready for execution.
    /// Can be called directly by tests to verify processing logic deterministically.
    /// </summary>
    public async Task<int> ProcessPendingBatchAsync(CancellationToken cancellationToken = default)
    {
        var pendingCommands = await _commandStore.GetPendingQueueAsync(_clock.UtcNow, limit: 10, cancellationToken).ConfigureAwait(false);
        if (pendingCommands.Count == 0)
        {
            return 0;
        }

        foreach (var command in pendingCommands)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessSingleCommandAsync(command, cancellationToken).ConfigureAwait(false);
        }

        return pendingCommands.Count;
    }

    private async Task ProcessSingleCommandAsync(AiCommand command, CancellationToken cancellationToken)
    {
        _logger.Information("Processing AI command {CommandId} (#{DisplayNo}): {Text}", command.Id, command.DisplayNo, command.Text);

        // Mark as processing
        await _commandStore.UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Processing,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // FR-QUEUE-4: Context uses command.TypedAt as anchor for relative time expressions
        var promptContext = new PromptContext(
            TypedAt: command.TypedAt,
            TimeZoneId: command.TimeZone,
            Locale: string.Equals(command.InputLang, "en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "vi-VN");

        var orchestrator = _orchestratorFactory is not null ? _orchestratorFactory() : _orchestrator;
        if (orchestrator is null)
        {
            _logger.Warning("No chat orchestrator configured, reverting command {CommandId} to pending", command.Id);
            await _commandStore.UpdateStatusAsync(command.Id, AiCommandStatus.Pending, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        orchestrator.Reset();

        ChatOrchestratorState state;
        try
        {
            state = await orchestrator.ProcessUserMessageAsync(command.Text, promptContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Worker is stopping while executing; leave in 'processing' so it will recover on next startup (FR-QUEUE-8)
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error executing model for AI command {CommandId}", command.Id);
            await HandleFailureAsync(command, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (state)
        {
            case ChatOrchestratorState.Completed:
                await _commandStore.UpdateStatusAsync(
                    command.Id,
                    AiCommandStatus.Completed,
                    batchId: orchestrator.ExecutionResult?.BatchId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.Information("AI command {CommandId} completed successfully", command.Id);
                break;

            case ChatOrchestratorState.AwaitingConfirmation:
                // FR-QUEUE-7: Destructive or non-auto-confirmed actions stop at NeedsConfirmation
                await _commandStore.UpdateStatusAsync(
                    command.Id,
                    AiCommandStatus.NeedsConfirmation,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.Information("AI command {CommandId} requires manual confirmation", command.Id);
                break;

            case ChatOrchestratorState.AwaitingClarification:
                // FR-CONFIRM-8: Model asked for clarification
                await _commandStore.UpdateStatusAsync(
                    command.Id,
                    AiCommandStatus.NeedsConfirmation,
                    lastError: orchestrator.ClarificationQuestion,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.Information("AI command {CommandId} requires clarification from user", command.Id);
                break;

            case ChatOrchestratorState.Cancelled:
                await _commandStore.UpdateStatusAsync(
                    command.Id,
                    AiCommandStatus.Cancelled,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;

            default:
                await HandleFailureAsync(command, orchestrator.ErrorMessage ?? "Execution failed", cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleFailureAsync(AiCommand command, string errorMessage, CancellationToken cancellationToken)
    {
        var nextRetry = command.RetryCount + 1;
        var nextRetryAt = _retryPolicy.CalculateNextRetry(nextRetry, _clock.UtcNow);

        await _commandStore.UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: errorMessage,
            retryCount: nextRetry,
            nextRetryAt: nextRetryAt,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (nextRetryAt.HasValue)
        {
            _logger.Warning("AI command {CommandId} failed (attempt {RetryCount}/{MaxRetries}), retrying at {NextRetryAt}: {Error}",
                command.Id, nextRetry, AiQueueRetryPolicy.MaxRetries, nextRetryAt, errorMessage);
        }
        else
        {
            _logger.Error("AI command {CommandId} exceeded max retries ({MaxRetries}), manual intervention required: {Error}",
                command.Id, AiQueueRetryPolicy.MaxRetries, errorMessage);
        }
    }

    private async Task WaitForSignalOrDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await _signal.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, bool isOnline)
    {
        if (isOnline)
        {
            _logger.Information("Network restored; triggering AI queue worker");
            Trigger();
        }
    }

    public override void Dispose()
    {
        _networkStatus.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _signal.Dispose();
        base.Dispose();
    }
}
