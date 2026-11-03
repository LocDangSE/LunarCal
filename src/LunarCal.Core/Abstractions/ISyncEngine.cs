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
}
