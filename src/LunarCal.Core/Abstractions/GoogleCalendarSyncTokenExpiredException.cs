namespace LunarCal.Core.Abstractions;

/// <summary>
/// Exception thrown when Google Calendar returns HTTP 410 Gone for an expired or invalid syncToken (FR-SYNC-2).
/// Signals the caller that a full resync must be initiated.
/// </summary>
public sealed class GoogleCalendarSyncTokenExpiredException : Exception
{
    public string CalendarId { get; }

    public GoogleCalendarSyncTokenExpiredException(string calendarId, string? message = null, Exception? innerException = null)
        : base(message ?? $"SyncToken for calendar '{calendarId}' has expired or is invalid (HTTP 410 Gone). A full resync is required.", innerException)
    {
        CalendarId = calendarId;
    }
}
