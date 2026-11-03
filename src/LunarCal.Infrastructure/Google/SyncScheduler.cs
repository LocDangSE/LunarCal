using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Network;
using LunarCal.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Hosted background service that periodically synchronizes Google Calendar events
/// according to window state (2m active, 10m tray/background) and handles immediate triggers (FR-SYNC-3).
/// </summary>
public sealed class SyncScheduler : BackgroundService, ISyncScheduler
{
    private readonly ISyncEngine _syncEngine;
    private readonly ISqliteConnectionFactory? _connectionFactory;
    private readonly SyncOptions _syncOptions;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<List<string>>>? _accountIdProvider;

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _stateLock = new();

    private SyncScheduleState _currentState = SyncScheduleState.Active;
    private bool _isSyncInProgress;
    private DateTimeOffset? _lastSyncTime;
    private CancellationTokenSource? _wakeCts;

    /// <summary>
    /// Initializes a new instance of <see cref="SyncScheduler"/>.
    /// </summary>
    public SyncScheduler(
        ISyncEngine syncEngine,
        ISqliteConnectionFactory? connectionFactory = null,
        SyncOptions? syncOptions = null,
        IClock? clock = null,
        ILogger? logger = null,
        Func<CancellationToken, Task<List<string>>>? accountIdProvider = null)
    {
        _syncEngine = syncEngine ?? throw new ArgumentNullException(nameof(syncEngine));
        _connectionFactory = connectionFactory;
        _syncOptions = syncOptions ?? new SyncOptions();
        _clock = clock ?? new SystemClock();
        _logger = logger ?? Log.ForContext<SyncScheduler>();
        _accountIdProvider = accountIdProvider;
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

    /// <inheritdoc />
    public DateTimeOffset? LastSyncTime => _lastSyncTime;

    /// <inheritdoc />
    public event EventHandler<SyncResult>? SyncCompleted;

    /// <inheritdoc />
    public event EventHandler<SyncScheduleState>? StateChanged;

    /// <inheritdoc />
    public void SetState(SyncScheduleState state)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (_currentState != state)
            {
                _currentState = state;
                changed = true;
            }
        }

        if (changed)
        {
            _logger.Information("SyncScheduler state changed to {State}", state);
            StateChanged?.Invoke(this, state);

            // If switching to Active mode, wake the scheduler early so user doesn't wait up to 10m
            if (state == SyncScheduleState.Active)
            {
                WakeScheduler();
            }
        }
    }

    /// <inheritdoc />
    public Task TriggerFocusSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Debug("Sync triggered by window focus.");
        return RunImmediateSyncAsync("Focus", cancellationToken);
    }

    /// <inheritdoc />
    public Task TriggerPostMutationSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Debug("Sync triggered after write mutation.");
        return RunImmediateSyncAsync("PostMutation", cancellationToken);
    }

    /// <inheritdoc />
    public Task TriggerManualSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.Information("Manual sync triggered by user.");
        return RunImmediateSyncAsync("Manual", cancellationToken);
    }

    /// <summary>
    /// Gets the polling interval corresponding to the current schedule state.
    /// </summary>
    public TimeSpan GetCurrentInterval()
    {
        lock (_stateLock)
        {
            return _currentState == SyncScheduleState.Active
                ? TimeSpan.FromMinutes(_syncOptions.PollingActiveMinutes)
                : TimeSpan.FromMinutes(_syncOptions.PollingTrayMinutes);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("SyncScheduler background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteSyncPassCoreAsync("Polling", stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during scheduled sync pass.");
            }

            // Wait for next polling interval or wake signal
            var interval = GetCurrentInterval();
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                lock (_stateLock)
                {
                    _wakeCts = linkedCts;
                }

                await _clock.DelayAsync(interval, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Debug("SyncScheduler delay cancelled early by wake signal.");
            }
            finally
            {
                lock (_stateLock)
                {
                    _wakeCts = null;
                }
            }
        }

        _logger.Information("SyncScheduler background service stopped.");
    }

    private async Task RunImmediateSyncAsync(string triggerReason, CancellationToken cancellationToken)
    {
        // Wake the background delay so polling schedule is reset after this sync
        WakeScheduler();

        await ExecuteSyncPassCoreAsync(triggerReason, cancellationToken).ConfigureAwait(false);
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
                // Ignored if already disposed
            }
        }
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
            _logger.Debug("Executing sync pass ({Reason})...", reason);

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
