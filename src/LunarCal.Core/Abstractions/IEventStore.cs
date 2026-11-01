using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for querying and persisting calendar collections and events in storage (spec §6, FR-UI-1, FR-SYNC-6).
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Gets all calendars, optionally filtered by account identifier (FR-AUTH-5).
    /// </summary>
    /// <param name="accountId">Optional account identifier filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a calendar by its identifier.
    /// </summary>
    /// <param name="calendarId">The calendar identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CalendarInfo?> GetCalendarByIdAsync(string calendarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a calendar.
    /// </summary>
    /// <param name="calendar">The calendar to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertCalendarAsync(CalendarInfo calendar, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a calendar by its identifier.
    /// </summary>
    /// <param name="calendarId">The calendar identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteCalendarAsync(string calendarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events that overlap with the specified UTC interval, optionally filtered by calendar.
    /// </summary>
    /// <param name="startUtc">Start of the query interval (inclusive).</param>
    /// <param name="endUtc">End of the query interval (exclusive).</param>
    /// <param name="calendarId">Optional calendar identifier filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? calendarId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single event by its identifier.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CalendarEvent?> GetEventByIdAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates an event.
    /// </summary>
    /// <param name="calendarEvent">The event to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an event by its identifier.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default);
}
