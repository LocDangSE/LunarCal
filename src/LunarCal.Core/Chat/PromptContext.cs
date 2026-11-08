namespace LunarCal.Core.Chat;

/// <summary>
/// Execution context provided when rendering the system prompt (FR-CHAT-1, FR-QUEUE-4, spec §7, SEC-PRIV).
/// In accordance with privacy guarantees, this context contains metadata only (typed_at, timezone, locale, calendar names)
/// and MUST NEVER contain user calendar event contents.
/// </summary>
public sealed record PromptContext(
    DateTimeOffset TypedAt,
    string TimeZoneId,
    string Locale = "vi-VN",
    IReadOnlyList<string>? AvailableCalendars = null)
{
    /// <summary>
    /// Gets the formatted list of available calendars, defaulting to a standard primary calendar if none are specified.
    /// </summary>
    public string FormatAvailableCalendars()
    {
        if (AvailableCalendars == null || AvailableCalendars.Count == 0)
        {
            return "- primary (Default Google Calendar)";
        }

        return string.Join(Environment.NewLine, AvailableCalendars.Select(c => $"- {c}"));
    }
}
