namespace LunarCal.Core.Abstractions;

/// <summary>
/// Information about an event mutation conflict resolved by the server-wins policy (FR-SYNC-4).
/// </summary>
public sealed record EventConflictNotice(
    string AccountId,
    string CalendarId,
    string EventId,
    string? EventTitle,
    string ConflictReason,
    DateTimeOffset OccurredAt);

/// <summary>
/// Port for publishing and subscribing to conflict notices when remote server wins over local mutation (FR-SYNC-4).
/// </summary>
public interface IConflictNotifier
{
    /// <summary>
    /// Event raised when a server-wins conflict occurs and local changes are replaced.
    /// </summary>
    event EventHandler<EventConflictNotice>? ConflictOccurred;

    /// <summary>
    /// Notifies listeners that a mutation conflict occurred.
    /// </summary>
    /// <param name="notice">Details of the conflict.</param>
    void NotifyConflict(EventConflictNotice notice);
}
