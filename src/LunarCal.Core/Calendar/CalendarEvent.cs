namespace LunarCal.Core.Calendar;

/// <summary>
/// Represents a calendar event stored locally or synchronized with Google Calendar (spec §6, FR-SYNC-6, FR-UI-1).
/// </summary>
public sealed record CalendarEvent
{
    /// <summary>
    /// Local unique identifier (spec §6).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Foreign key referencing the parent <see cref="CalendarInfo.Id"/> (spec §6).
    /// </summary>
    public required string CalendarId { get; init; }

    /// <summary>
    /// Remote Google Calendar event ID if synchronized (spec §6, FR-SYNC-6).
    /// </summary>
    public string? GoogleId { get; init; }

    /// <summary>
    /// Identifier of the recurring series if this is an occurrence (spec §6, FR-SYNC-6).
    /// </summary>
    public string? RecurringEventId { get; init; }

    /// <summary>
    /// Event title or summary (spec §6).
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed description or notes for the event (spec §6).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Location of the event (spec §6).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Start time in UTC (spec §6).
    /// </summary>
    public required DateTimeOffset StartUtc { get; init; }

    /// <summary>
    /// End time in UTC (spec §6).
    /// </summary>
    public required DateTimeOffset EndUtc { get; init; }

    /// <summary>
    /// Whether this is an all-day event (spec §6).
    /// </summary>
    public bool AllDay { get; init; }

    /// <summary>
    /// Timezone identifier of the event, e.g. "Asia/Ho_Chi_Minh" (spec §6).
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Google ETag for optimistic concurrency control (spec §6, FR-SYNC-6, FR-SYNC-4).
    /// </summary>
    public string? Etag { get; init; }

    /// <summary>
    /// Timestamp of the last update in UTC (spec §6, FR-SYNC-6).
    /// </summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>
    /// Event status: confirmed, tentative, cancelled (spec §6).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Extended properties dictionary (spec §6 extended_json, FR-SYNC-6 private extendedProperties).
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtendedProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Materialized lunar rule ID if this event originated from a lunar rule (FR-LUNAR-5, FR-SYNC-6).
    /// </summary>
    public string? LunarRuleId => ExtendedProperties.TryGetValue("lunarRuleId", out var val) ? val : null;

    /// <summary>
    /// Creator identifier, e.g. "lunarcal-chat" for AI created events (FR-CHAT-5, FR-SYNC-6).
    /// </summary>
    public string? CreatedBy => ExtendedProperties.TryGetValue("createdBy", out var val) ? val : null;

    /// <summary>
    /// Batch identifier for bulk operations and undo support (FR-CHAT-5, FR-SYNC-6).
    /// </summary>
    public string? BatchId => ExtendedProperties.TryGetValue("batchId", out var val) ? val : null;
}
