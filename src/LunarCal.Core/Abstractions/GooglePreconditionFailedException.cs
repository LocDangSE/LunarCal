namespace LunarCal.Core.Abstractions;

/// <summary>
/// Exception thrown when Google Calendar returns HTTP 412 Precondition Failed (FR-SYNC-4).
/// Signals an ETag / If-Match mismatch due to concurrent modifications on the server.
/// Per spec FR-SYNC-4, server wins when conflict occurs, and the user must be notified.
/// </summary>
public sealed class GooglePreconditionFailedException : Exception
{
    /// <summary>Calendar identifier of the conflicted event.</summary>
    public string CalendarId { get; }

    /// <summary>Event identifier of the conflicted event.</summary>
    public string EventId { get; }

    /// <summary>The expected ETag sent in the If-Match header.</summary>
    public string? ExpectedEtag { get; }

    public GooglePreconditionFailedException(
        string calendarId,
        string eventId,
        string? expectedEtag = null,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? $"Precondition failed (HTTP 412) for event '{eventId}' on calendar '{calendarId}'. Server version differs from expected ETag '{expectedEtag}'.", innerException)
    {
        CalendarId = calendarId;
        EventId = eventId;
        ExpectedEtag = expectedEtag;
    }
}
