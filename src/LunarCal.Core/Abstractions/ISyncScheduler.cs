namespace LunarCal.Core.Abstractions;

/// <summary>
/// Application window state for sync scheduling policy (FR-SYNC-3).
/// </summary>
public enum SyncScheduleState
{
    /// <summary>
    /// The main application window is open and active (polling interval: 2 minutes).
    /// </summary>
    Active,

    /// <summary>
    /// The application window is minimized to system tray or in background (polling interval: 10 minutes).
    /// </summary>
    TrayOrBackground
}

/// <summary>
/// Scheduler for automatic background synchronization and immediate sync triggers (FR-SYNC-3).
/// </summary>
public interface ISyncScheduler
{
    /// <summary>
    /// Gets the current schedule state (Active or TrayOrBackground).
    /// </summary>
    SyncScheduleState CurrentState { get; }

    /// <summary>
    /// Gets whether a synchronization pass is currently in progress.
    /// </summary>
    bool IsSyncInProgress { get; }

    /// <summary>
    /// Gets the UTC timestamp of the last successful synchronization pass.
    /// </summary>
    DateTimeOffset? LastSyncTime { get; }

    /// <summary>
    /// Sets the application state, dynamically adjusting the polling interval (2m active, 10m tray).
    /// </summary>
    void SetState(SyncScheduleState state);

    /// <summary>
    /// Triggers an immediate sync pass (e.g. When the main window receives focus).
    /// Does not duplicate an ongoing sync pass.
    /// </summary>
    Task TriggerFocusSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an immediate sync pass after a write mutation has completed.
    /// </summary>
    Task TriggerPostMutationSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers an immediate manual sync pass (e.g. User clicked "Sync now").
    /// </summary>
    Task TriggerManualSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a sync pass begins.
    /// </summary>
    event EventHandler? SyncStarted;

    /// <summary>
    /// Event raised when a sync pass completes, carrying the sync result.
    /// </summary>
    event EventHandler<SyncResult>? SyncCompleted;

    /// <summary>
    /// Event raised when the sync schedule state changes.
    /// </summary>
    event EventHandler<SyncScheduleState>? StateChanged;
}
