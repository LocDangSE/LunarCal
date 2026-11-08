using System;
using System.Globalization;
using System.Text.Json;
using LunarCal.Core.Chat;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Values the event editor starts with when the user presses "Sửa" in the AI confirmation dialog (FR-CONFIRM-4, P4-10):
/// what the model proposed, as wall-clock time in the zone of the command.
/// </summary>
public sealed record EventEditorPrefill(string Title, DateTime Start, DateTime End, bool IsAllDay)
{
    /// <summary>
    /// Builds a prefill from a proposed <c>create_event</c> call, or returns <c>null</c> when the call has nothing to show
    /// (not a JSON object, no start). Times are converted to the zone saved with the command.
    /// </summary>
    public static EventEditorPrefill? FromToolCall(ToolCall? call, string? zoneId)
    {
        if (call is null || string.IsNullOrWhiteSpace(call.ArgumentsJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(call.ArgumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("start", out var startProp) ||
                !DateTimeOffset.TryParse(startProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            {
                return null;
            }

            var localStart = AiCommandTime.InZone(start, zoneId);
            var allDay = ToolArgumentReader.IsAllDay(root);
            var localEnd = root.TryGetProperty("end", out var endProp) &&
                           DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                ? AiCommandTime.InZone(end, zoneId)
                : localStart.AddHours(1);

            return new EventEditorPrefill(
                ToolArgumentReader.GetTitle(root) ?? string.Empty,
                localStart.DateTime,
                localEnd.DateTime,
                allDay);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
