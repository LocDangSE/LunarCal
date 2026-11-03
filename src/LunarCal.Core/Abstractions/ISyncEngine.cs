namespace LunarCal.Core.Abstractions;

/// <summary>
/// Synchronization engine coordinating calendar data sync between Google Calendar API and local SQLite cache (FR-SYNC-1).
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Executes the initial sync for an account: fetches all calendars, then events within [-12 months, +24 months],
    /// and writes them to local storage (FR-SYNC-1, FR-SYNC-6).
    /// </summary>
    Task<SyncResult> SyncInitialAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an incremental sync for an account using stored sync tokens (FR-SYNC-2).
    /// If a sync token has expired (HTTP 410 Gone), automatically triggers a full resync for that calendar (FR-SYNC-2).
    /// Deletes events marked with status 'cancelled' and upserts modified/added events (FR-SYNC-2, FR-SYNC-6).
    /// </summary>
    Task<SyncResult> SyncIncrementalAsync(string accountId, CancellationToken cancellationToken = default);
}
