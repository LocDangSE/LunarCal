using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of listing calendars from Google Calendar API (FR-SYNC-1).
/// </summary>
public sealed record CalendarListResult
{
    public required IReadOnlyList<CalendarInfo> Calendars { get; init; }
    public string? NextPageToken { get; init; }
    public string? NextSyncToken { get; init; }
    public string? Etag { get; init; }
}
