namespace LunarCal.Core.Calendar;

/// <summary>
/// Represents a calendar collection/source belonging to an account (spec §6, FR-AUTH-5, FR-UI-1).
/// </summary>
public sealed record CalendarInfo
{
    /// <summary>
    /// Unique identifier for the calendar.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Account identifier that owns this calendar (FR-AUTH-5 multi-account ready).
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Human-readable title or summary of the calendar.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Hex color code (e.g. #4285F4) or color id for UI display.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Whether events from this calendar are selected (visible) in the UI (spec §6).
    /// </summary>
    public bool IsSelected { get; init; } = true;

    /// <summary>
    /// Google Calendar sync token for incremental synchronization (FR-SYNC-2, spec §6).
    /// </summary>
    public string? SyncToken { get; init; }

    /// <summary>
    /// Whether this is the account's primary calendar.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Whether this calendar is read-only (e.g. subscribed holiday calendar).
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Time zone identifier of the calendar (e.g. "Asia/Ho_Chi_Minh").
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Optional description of the calendar.
    /// </summary>
    public string? Description { get; init; }
}
