namespace LunarCal.Core.Abstractions;

/// <summary>
/// Gateway port for interacting with the Google Calendar API v3 (FR-SYNC-1, FR-SYNC-6).
/// </summary>
public interface ICalendarGateway
{
    /// <summary>
    /// Retrieves user's calendar list (users/me/calendarList).
    /// </summary>
    Task<CalendarListResult> GetCalendarListAsync(
        string accessToken,
        string? pageToken = null,
        string? syncToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events from a specific calendar (calendars/{calendarId}/events).
    /// Expands recurring instances when singleEvents is true (FR-SYNC-1).
    /// </summary>
    Task<EventsListResult> GetEventsAsync(
        string accessToken,
        string calendarId,
        bool singleEvents = true,
        DateTimeOffset? timeMin = null,
        DateTimeOffset? timeMax = null,
        string? pageToken = null,
        string? syncToken = null,
        CancellationToken cancellationToken = default);
}
