using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of listing events from Google Calendar API (FR-SYNC-1, FR-SYNC-6).
/// </summary>
public sealed record EventsListResult
{
    public required IReadOnlyList<CalendarEvent> Events { get; init; }
    public string? NextPageToken { get; init; }
    public string? NextSyncToken { get; init; }
    public string? Etag { get; init; }
}
