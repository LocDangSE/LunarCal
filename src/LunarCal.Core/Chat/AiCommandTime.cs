using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LunarCal.Core.Chat;

/// <summary>
/// Time zone helpers for AI commands (FR-QUEUE-4, FR-CONFIRM-1). The queue stores <c>typed_at</c> as a UTC instant, so
/// the local offset the user typed in is lost on read. Every consumer (prompt, confirmation dialog) re-anchors the
/// instant in the zone saved with the command, so "6h tối mai" is shown and sent as 18:00 local and never as UTC.
/// </summary>
public static class AiCommandTime
{
    /// <summary>Finds a zone by Windows or IANA id; returns <c>null</c> when the id is empty or unknown.</summary>
    public static TimeZoneInfo? TryFindZone(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the same instant expressed with the offset of <paramref name="zoneId"/> at that instant (DST aware).
    /// An unknown zone leaves the value unchanged.
    /// </summary>
    public static DateTimeOffset InZone(DateTimeOffset value, string? zoneId)
    {
        var zone = TryFindZone(zoneId);
        return zone is null ? value : TimeZoneInfo.ConvertTime(value, zone);
    }

    /// <summary>
    /// Describes the zone for the model, for example <c>SE Asia Standard Time (UTC+07:00)</c>, so the offset is explicit
    /// and the model does not have to guess what the zone id means.
    /// </summary>
    public static string DescribeZone(string? zoneId, DateTimeOffset at)
    {
        var zone = TryFindZone(zoneId);
        if (zone is null)
        {
            return string.IsNullOrWhiteSpace(zoneId) ? $"UTC{FormatOffset(at.Offset)}" : zoneId.Trim();
        }

        // The id is kept as saved (a Windows id maps to a different IANA city than the user may expect); the numeric
        // offset is what the model needs to compute absolute times.
        return string.Create(CultureInfo.InvariantCulture, $"{zone.Id} (UTC{FormatOffset(zone.GetUtcOffset(at))})");
    }

    /// <summary>Adds <c>timezone</c> to the arguments when the model did not send one; never overwrites it.</summary>
    public static ToolCall WithDefaultTimeZone(ToolCall call, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || string.IsNullOrWhiteSpace(call.ArgumentsJson))
        {
            return call;
        }

        try
        {
            if (JsonNode.Parse(call.ArgumentsJson) is not JsonObject args ||
                !string.IsNullOrWhiteSpace(args["timezone"]?.GetValue<string>()))
            {
                return call;
            }

            args["timezone"] = timeZone;
            return call with { ArgumentsJson = args.ToJsonString() };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return call; // Not a JSON object, or timezone is not a string: let the validator and executor deal with it.
        }
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var abs = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours:00}:{abs.Minutes:00}");
    }
}
