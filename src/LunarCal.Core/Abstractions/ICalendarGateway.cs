using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Gateway port for interacting with the Google Calendar API v3 (FR-SYNC-1, FR-SYNC-4, FR-SYNC-6, ADR-0028).
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

    /// <summary>
    /// Creates a new event in the specified calendar (calendars/{calendarId}/events, FR-SYNC-4, SEC-NET-2).
    /// </summary>
    Task<CalendarEvent> CreateEventAsync(
        string accessToken,
        string calendarId,
        CalendarEvent calendarEvent,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing event in the specified calendar (calendars/{calendarId}/events/{eventId}, FR-SYNC-4).
    /// Sends an If-Match header with expectedEtag; throws <see cref="GooglePreconditionFailedException"/> on HTTP 412.
    /// </summary>
    Task<CalendarEvent> UpdateEventAsync(
        string accessToken,
        string calendarId,
        CalendarEvent calendarEvent,
        string? expectedEtag = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing event from the specified calendar (calendars/{calendarId}/events/{eventId}, FR-SYNC-4).
    /// Sends an If-Match header with expectedEtag; throws <see cref="GooglePreconditionFailedException"/> on HTTP 412.
    /// </summary>
    Task DeleteEventAsync(
        string accessToken,
        string calendarId,
        string eventId,
        string? expectedEtag = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single event by ID from the specified calendar (calendars/{calendarId}/events/{eventId}).
    /// Returns null if the event does not exist on the server (HTTP 404).
    /// </summary>
    Task<CalendarEvent?> GetEventAsync(
        string accessToken,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken = default);
}
