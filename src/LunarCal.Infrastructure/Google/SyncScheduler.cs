using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Network;
using LunarCal.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Hosted background service that periodically synchronizes Google Calendar events
/// according to window state (2m active, 10m tray/background) and handles immediate triggers (FR-SYNC-3, FR-SYNC-4).
/// </summary>
public sealed class SyncScheduler : BackgroundService, ISyncScheduler
{
    private readonly ISyncEngine _syncEngine;
    private readonly ISqliteConnectionFactory? _connectionFactory;
    private readonly SyncOptions _syncOptions;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<List<string>>>? _accountIdProvider;
    private IMutationService? _mutationService;

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _stateLock = new();

    private SyncScheduleState _currentState = SyncScheduleState.Active;
    private bool _isSyncInProgress;
    private DateTimeOffset? _lastSyncTime;
    private CancellationTokenSource? _wakeCts;

    /// <summary>
    /// Optional mutation service to flush pending mutations before each sync pass (FR-SYNC-4).
    /// </summary>
    public IMutationService? MutationService
    {
        get => _mutationService;
        set => _mutationService = value;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SyncScheduler"/>.
    /// </summary>
    public SyncScheduler(
        ISyncEngine syncEngine,
        ISqliteConnectionFactory? connectionFactory = null,
        SyncOptions? syncOptions = null,
        IClock? clock = null,
        ILogger? logger = null,
        Func<CancellationToken, Task<List<string>>>? accountIdProvider = null,
        IMutationService? mutationService = null)
    {
        _syncEngine = syncEngine ?? throw new ArgumentNullException(nameof(syncEngine));
        _connectionFactory = connectionFactory;
        _syncOptions = syncOptions ?? new SyncOptions();
        _clock = clock ?? new SystemClock();
        _logger = logger ?? Log.ForContext<SyncScheduler>();
        _accountIdProvider = accountIdProvider;
        _mutationService = mutationService;
    }

    /// <inheritdoc />
    public SyncScheduleState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    /// <inheritdoc />
    public bool IsSyncInProgress => _isSyncInProgress;

    /// <summary>
    /// Gets the current polling interval based on the active state.
    /// </summary>
    public TimeSpan GetCurrentInterval()
    {
        var minutes = CurrentState == SyncScheduleState.Active
            ? _syncOptions.PollingActiveMinutes
            : _syncOptions.PollingTrayMinutes;
        return TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    /// <inheritdoc />
    public DateTimeOffset? LastSyncTime => _lastSyncTime;

    /// <inheritdoc />
    public event EventHandler? SyncStarted;

    /// <inheritdoc />
    public event EventHandler<SyncResult>? SyncCompleted;

    /// <inheritdoc />
    public event EventHandler<SyncScheduleState>? StateChanged;

    /// <inheritdoc />
    public void SetState(SyncScheduleState state)
    {
        lock (_stateLock)
        {
            if (_currentState == state)
            {
                return;
            }

            _logger.Information("SyncScheduler state changed from {OldState} to {NewState} (FR-SYNC-3)", _currentState, state);
            _currentState = state;
            StateChanged?.Invoke(this, state);

            // If transitioning to Active, wake immediately to catch up
            if (state == SyncScheduleState.Active)
            {
                WakeScheduler();
            }
        }
    }

    /// <inheritdoc />
    public async Task TriggerFocusSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Immediate focus sync triggered (FR-SYNC-3)");
        await ExecuteSyncPassCoreAsync("Focus", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task TriggerPostMutationSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Immediate post-mutation sync triggered (FR-SYNC-3)");
        await ExecuteSyncPassCoreAsync("PostMutation", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task TriggerManualSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Immediate manual sync triggered (FR-SYNC-3)");
        await ExecuteSyncPassCoreAsync("Manual", cancellationToken).ConfigureAwait(false);
    }

    private void WakeScheduler()
    {
        lock (_stateLock)
        {
            try
            {
                _wakeCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("SyncScheduler background service started (FR-SYNC-3)");

        // Initial sync on startup
        try
        {
            await ExecuteSyncPassCoreAsync("InitialStartup", stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Initial sync pass on startup failed; scheduler will continue to poll.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = CurrentState == SyncScheduleState.Active
                ? _syncOptions.PollingActiveMinutes
                : _syncOptions.PollingTrayMinutes;

            var delayDuration = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));

            CancellationTokenSource linkedCts;
            lock (_stateLock)
            {
                _wakeCts = new CancellationTokenSource();
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _wakeCts.Token);
            }

            try
            {
                _logger.Debug("SyncScheduler waiting for next polling interval ({IntervalMinutes}m, state={State})",
                    intervalMinutes, CurrentState);

                await _clock.DelayAsync(delayDuration, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Debug("SyncScheduler woke early due to trigger or state transition.");
            }
            finally
            {
                linkedCts.Dispose();
                lock (_stateLock)
                {
                    _wakeCts?.Dispose();
                    _wakeCts = null;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ExecuteSyncPassCoreAsync("PeriodicPolling", stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error occurred during periodic sync pass; scheduler will retry on next cycle.");
            }
        }

        _logger.Information("SyncScheduler background service stopped.");
    }

    private async Task ExecuteSyncPassCoreAsync(string reason, CancellationToken cancellationToken)
    {
        // Avoid duplicate overlapping sync executions
        if (!await _syncLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _logger.Debug("Sync pass ({Reason}) skipped because another sync pass is already in progress.", reason);
            return;
        }

        try
        {
            _isSyncInProgress = true;
            SyncStarted?.Invoke(this, EventArgs.Empty);
            _logger.Debug("Executing sync pass ({Reason})...", reason);

            // Flush pending mutations before sync pass (FR-SYNC-4)
            if (_mutationService != null)
            {
                try
                {
                    await _mutationService.ProcessPendingMutationsAsync(null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to process pending mutations during sync pass ({Reason})", reason);
                }
            }

            var accounts = await GetAccountIdsAsync(cancellationToken).ConfigureAwait(false);
            if (accounts.Count == 0)
            {
                _logger.Debug("No accounts configured in local database; skipping sync.");
                return;
            }

            foreach (var accountId in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await _syncEngine.SyncIncrementalAsync(accountId, cancellationToken).ConfigureAwait(false);
                    _lastSyncTime = _clock.UtcNow;
                    SyncCompleted?.Invoke(this, result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to synchronize account {AccountId} during pass ({Reason})", accountId, reason);
                }
            }
        }
        finally
        {
            _isSyncInProgress = false;
            _syncLock.Release();
        }
    }

    private async Task<List<string>> GetAccountIdsAsync(CancellationToken cancellationToken)
    {
        if (_accountIdProvider != null)
        {
            return await _accountIdProvider(cancellationToken).ConfigureAwait(false);
        }

        if (_connectionFactory == null)
        {
            return new List<string>();
        }

        var list = new List<string>();
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM Account;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    list.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error querying Account table for sync accounts.");
        }

        return list;
    }
}
