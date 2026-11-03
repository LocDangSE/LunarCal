using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of processing the pending offline mutations queue (FR-SYNC-4, NFR-REL).
/// </summary>
public sealed record MutationProcessingResult(
    int ProcessedCount,
    int SucceededCount,
    int FailedCount,
    int ConflictCount,
    bool IsOffline,
    string? Message)
{
    public static MutationProcessingResult Empty => new(0, 0, 0, 0, false, null);
}

/// <summary>
/// Service managing optimistic UI writes, offline queuing and synchronization dispatching (FR-SYNC-4, NFR-REL).
/// </summary>
public interface IMutationService
{
    /// <summary>
    /// Optimistically persists a new event locally and enqueues a remote CreateEvent mutation.
    /// </summary>
    Task<CalendarEvent> CreateEventOptimisticAsync(
        string accountId,
        string calendarId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimistically updates an event locally and enqueues a remote UpdateEvent mutation with ETag check.
    /// </summary>
    Task<CalendarEvent> UpdateEventOptimisticAsync(
        string accountId,
        string calendarId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimistically deletes an event locally and enqueues a remote DeleteEvent mutation with ETag check.
    /// </summary>
    Task DeleteEventOptimisticAsync(
        string accountId,
        string calendarId,
        string eventId,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes queued mutations in FIFO order, dispatching them to the remote provider with retry and conflict handling.
    /// </summary>
    Task<MutationProcessingResult> ProcessPendingMutationsAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default);
}
