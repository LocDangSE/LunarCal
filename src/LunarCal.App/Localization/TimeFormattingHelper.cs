using System.Globalization;

namespace LunarCal.App.Localization;

/// <summary>
/// Helper for culture-aware time and time range formatting supporting 12h and 24h formats (FR-UI-7, FR-I18N-4).
/// </summary>
public static class TimeFormattingHelper
{
    /// <summary>Constant for 24-hour time format identifier.</summary>
    public const string Format24Hours = "24h";

    /// <summary>Constant for 12-hour AM/PM time format identifier.</summary>
    public const string Format12Hours = "12h";

    /// <summary>
    /// Gets the standard format string pattern for formatting time based on format setting and culture.
    /// </summary>
    public static string GetTimeFormatPattern(string? timeFormat, CultureInfo? culture = null)
    {
        var is12H = string.Equals(timeFormat, Format12Hours, StringComparison.OrdinalIgnoreCase);
        if (is12H)
        {
            var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
            // Culture-aware 12-hour format with AM/PM designator
            return effectiveCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                ? "hh:mm tt"
                : "h:mm tt";
        }

        return "HH:mm";
    }

    /// <summary>
    /// Formats a <see cref="TimeOnly"/> according to the specified time format ("12h" or "24h") and culture.
    /// </summary>
    public static string FormatTime(TimeOnly time, string? timeFormat, CultureInfo? culture = null)
    {
        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        var pattern = GetTimeFormatPattern(timeFormat, effectiveCulture);
        return time.ToString(pattern, effectiveCulture);
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> according to the specified time format ("12h" or "24h") and culture.
    /// </summary>
    public static string FormatTime(DateTimeOffset dateTime, string? timeFormat, CultureInfo? culture = null)
    {
        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        var pattern = GetTimeFormatPattern(timeFormat, effectiveCulture);
        return dateTime.ToString(pattern, effectiveCulture);
    }

    /// <summary>
    /// Formats a time range (e.g. "09:00 – 10:30" or "9:00 AM – 10:30 AM") according to the specified format and culture.
    /// </summary>
    public static string FormatTimeRange(
        DateTimeOffset start,
        DateTimeOffset end,
        string? timeFormat,
        CultureInfo? culture = null)
    {
        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        var startStr = FormatTime(start, timeFormat, effectiveCulture);
        var endStr = FormatTime(end, timeFormat, effectiveCulture);
        return $"{startStr} – {endStr}";
    }
}
