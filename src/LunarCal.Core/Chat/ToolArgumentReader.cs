using System.Text.Json;

namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CHAT-1, FR-CONFIRM-1: Reads tool-call arguments using the field names of the schemas in <see cref="ToolDefinitions"/>
/// (<c>title</c>, <c>allDay</c>, <c>eventId</c>). The legacy spellings (<c>summary</c>, <c>all_day</c>, <c>event_id</c>)
/// are still accepted so older stored proposals keep working. The business validator, the confidence heuristic and the
/// confirmation dialog must all read through here, otherwise a real model call is judged "missing title" (P4-REVIEW).
/// </summary>
public static class ToolArgumentReader
{
    /// <summary>Gets the event title (<c>title</c>, falling back to <c>summary</c>), or null when absent or blank.</summary>
    public static string? GetTitle(JsonElement arguments) =>
        GetNonBlankString(arguments, "title") ?? GetNonBlankString(arguments, "summary");

    /// <summary>Gets the event id (<c>eventId</c>, falling back to <c>event_id</c>), or null when absent or blank.</summary>
    public static string? GetEventId(JsonElement arguments) =>
        GetNonBlankString(arguments, "eventId") ?? GetNonBlankString(arguments, "event_id");

    /// <summary>True when <c>allDay</c> (or legacy <c>all_day</c>) is the JSON value <c>true</c>.</summary>
    public static bool IsAllDay(JsonElement arguments) =>
        IsTrue(arguments, "allDay") || IsTrue(arguments, "all_day");

    private static bool IsTrue(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object &&
        arguments.TryGetProperty(name, out var prop) &&
        prop.ValueKind == JsonValueKind.True;

    private static string? GetNonBlankString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
