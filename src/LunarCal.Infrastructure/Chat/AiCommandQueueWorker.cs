using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-QUEUE-1..9, FR-CHAT-7: Background hosted service that processes AI commands from the SQLite queue.
/// Supports 6 lifecycle states, exponential backoff with jitter on errors (up to 5 retries),
/// crash recovery (processing -> pending), idempotency tracking, 30-day automatic retention cleanup,
/// and structured error mapping (stops unrecoverable errors from retrying, preserves offline commands).
/// </summary>
public sealed class AiCommandQueueWorker : BackgroundService
{
    private readonly IAiCommandStore _commandStore;
    private readonly Func<IChatOrchestrator>? _orchestratorFactory;
    private readonly IChatOrchestrator? _orchestrator;
    private readonly INetworkStatusService _networkStatus;
    private readonly IClock _clock;
    private readonly IAiChatErrorMapper _errorMapper;
    private readonly AiQueueWorkerOptions _options;
    private readonly AiQueueRetryPolicy _retryPolicy;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>
    /// Event raised when an AI execution error occurs and is mapped (FR-CHAT-7).
    /// </summary>
    public event EventHandler<AiChatErrorResult>? AiErrorOccurred;

    /// <summary>
    /// Gets the error mapper used by this worker.
    /// </summary>
    public IAiChatErrorMapper ErrorMapper => _errorMapper;

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
        ILogger? logger = null,
        IAiChatErrorMapper? errorMapper = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _errorMapper = errorMapper ?? new AiChatErrorMapper();
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
        ILogger? logger = null,
        IAiChatErrorMapper? errorMapper = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _orchestratorFactory = orchestratorFactory ?? throw new ArgumentNullException(nameof(orchestratorFactory));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _errorMapper = errorMapper ?? new AiChatErrorMapper();
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
        ILogger? logger = null,
        IAiChatErrorMapper? errorMapper = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _errorMapper = errorMapper ?? new AiChatErrorMapper();
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

        // Only commands that moved forward count: a command that stays pending (no orchestrator, network down) must not
        // make the worker loop spin without waiting (P4-REVIEW).
        var progressed = 0;
        foreach (var command in pendingCommands)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await ProcessSingleCommandAsync(command, cancellationToken).ConfigureAwait(false))
            {
                progressed++;
            }
        }

        return progressed;
    }

    private async Task<bool> ProcessSingleCommandAsync(AiCommand command, CancellationToken cancellationToken)
    {
        var orchestrator = _orchestratorFactory is not null ? _orchestratorFactory() : _orchestrator;
        if (orchestrator is null)
        {
            _logger.Warning("No chat orchestrator configured; AI command {CommandId} stays pending", command.Id);
            return false;
        }

        // SEC-LOG-2: chat content is only logged at Debug (off by default).
        _logger.Information("Processing AI command {CommandId} (#{DisplayNo})", command.Id, command.DisplayNo);
        _logger.Debug("AI command {CommandId} text: {Text}", command.Id, command.Text);

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

        orchestrator.Reset();

        // FR-CONFIRM-8: after the user answered the model's question, send the original text together with the exchange.
        var message = command.Proposal is { ClarificationAnswer: { Length: > 0 } answer }
            ? string.Concat(command.Text, "\n\n", command.Proposal.ClarificationQuestion, "\n", answer)
            : command.Text;

        ChatOrchestratorState state;
        try
        {
            state = await orchestrator.ProcessUserMessageAsync(message, promptContext, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Worker is stopping while executing; leave in 'processing' so it will recover on next startup (FR-QUEUE-8)
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error executing model for AI command {CommandId}", command.Id);
            var errorResult = _errorMapper.Map(ex);
            await HandleFailureAsync(command, errorResult, cancellationToken).ConfigureAwait(false);
            return errorResult.Kind != AiChatErrorKind.NetworkUnavailable;
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
                // FR-QUEUE-7: Destructive or non-auto-confirmed actions stop at NeedsConfirmation. The proposal is
                // persisted first so the confirmation dialog can show (and later run) exactly what the model proposed.
                await _commandStore.SetProposalAsync(
                    command.Id,
                    new AiCommandProposal(orchestrator.PendingToolCall),
                    cancellationToken).ConfigureAwait(false);
                await _commandStore.UpdateStatusAsync(
                    command.Id,
                    AiCommandStatus.NeedsConfirmation,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.Information("AI command {CommandId} requires manual confirmation", command.Id);
                break;

            case ChatOrchestratorState.AwaitingClarification:
                // FR-CONFIRM-8: Model asked for clarification
                await _commandStore.SetProposalAsync(
                    command.Id,
                    new AiCommandProposal(null, orchestrator.ClarificationQuestion),
                    cancellationToken).ConfigureAwait(false);
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
                var errorResult = _errorMapper.MapMessage(orchestrator.ErrorMessage ?? "Execution failed");
                await HandleFailureAsync(command, errorResult, cancellationToken).ConfigureAwait(false);
                return errorResult.Kind != AiChatErrorKind.NetworkUnavailable;
        }

        return true;
    }

    /// <summary>
    /// Handles AI command failure using structured error mapping (FR-CHAT-7).
    /// </summary>
    public async Task HandleFailureAsync(AiCommand command, AiChatErrorResult errorResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(errorResult);

        AiErrorOccurred?.Invoke(this, errorResult);

        // 1. Offline / Network unavailable: preserve in queue as pending, wait for network restoration (FR-CHAT-7)
        if (errorResult.Kind == AiChatErrorKind.NetworkUnavailable)
        {
            await _commandStore.UpdateStatusAsync(
                command.Id,
                AiCommandStatus.Pending,
                lastError: errorResult.TechnicalDetails ?? errorResult.MessageKey,
                nextRetryAt: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.Information("AI command {CommandId} preserved locally in queue waiting for network reconnection", command.Id);
            return;
        }

        // 2. Unrecoverable errors (invalid key, leaked key, safety blocked, daily quota exhausted, precondition failed):
        // Do not burn retry attempts, mark as Error with nextRetryAt = null (FR-CHAT-7, ADR-0016 §4).
        if (!errorResult.CanRetry)
        {
            await _commandStore.UpdateStatusAsync(
                command.Id,
                AiCommandStatus.Error,
                lastError: errorResult.TechnicalDetails ?? errorResult.MessageKey,
                retryCount: command.RetryCount,
                nextRetryAt: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.Warning("AI command {CommandId} failed permanently with {ErrorKind} (action: {Action}): {Details}",
                command.Id, errorResult.Kind, errorResult.Action, errorResult.TechnicalDetails);
            return;
        }

        // 3. Recoverable errors (RateLimitExceeded, TransientError):
        var nextRetry = command.RetryCount + 1;
        DateTimeOffset? nextRetryAt;

        if (errorResult.Kind == AiChatErrorKind.RateLimitExceeded && errorResult.RetryAfter.HasValue)
        {
            // Respect rate limit cooldown period
            nextRetryAt = _clock.UtcNow.Add(errorResult.RetryAfter.Value);
        }
        else
        {
            nextRetryAt = _retryPolicy.CalculateNextRetry(nextRetry, _clock.UtcNow);
        }

        await _commandStore.UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: errorResult.TechnicalDetails ?? errorResult.MessageKey,
            retryCount: nextRetry,
            nextRetryAt: nextRetryAt,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (nextRetryAt.HasValue)
        {
            _logger.Warning("AI command {CommandId} failed (attempt {RetryCount}/{MaxRetries}), retrying at {NextRetryAt}: {Error}",
                command.Id, nextRetry, AiQueueRetryPolicy.MaxRetries, nextRetryAt, errorResult.TechnicalDetails);
        }
        else
        {
            _logger.Error("AI command {CommandId} exceeded max retries ({MaxRetries}), manual intervention required: {Error}",
                command.Id, AiQueueRetryPolicy.MaxRetries, errorResult.TechnicalDetails);
        }
    }

    /// <summary>
    /// Legacy overload mapping raw error message string.
    /// </summary>
    public Task HandleFailureAsync(AiCommand command, string errorMessage, CancellationToken cancellationToken = default)
    {
        var errorResult = _errorMapper.MapMessage(errorMessage);
        return HandleFailureAsync(command, errorResult, cancellationToken);
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
