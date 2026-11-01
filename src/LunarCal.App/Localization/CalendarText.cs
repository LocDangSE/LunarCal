using System.Globalization;
using LunarCal.Core.Lunar;

namespace LunarCal.App.Localization;

/// <summary>
/// Builds every date, weekday and lunar label shown by the calendar views from resx patterns, so no UI wording
/// lives in view models (FR-I18N-3, FR-I18N-4, FR-I18N-5, FR-UI-2). Date patterns are .NET composite format
/// strings whose argument 0 (and 1 for ranges) is a <see cref="DateTime"/>, formatted in the current UI culture.
/// </summary>
public static class CalendarText
{
    private static readonly string[] _weekdayKeys = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>Month and year, e.g. "Tháng 11, 2026" / "November 2026".</summary>
    public static string MonthYear(ILocalizationService loc, DateOnly date) =>
        Format(loc, "format.month_year", ToDateTime(date));

    /// <summary>Weekday and full date, e.g. "Chủ Nhật, 1 Tháng 11, 2026" / "Sunday, November 1, 2026".</summary>
    public static string DayFull(ILocalizationService loc, DateOnly date) =>
        Format(loc, "format.day_full", ToDateTime(date));

    /// <summary>Date without weekday, e.g. "1 Tháng 11, 2026" / "Nov 1, 2026".</summary>
    public static string DayShort(ILocalizationService loc, DateOnly date) =>
        Format(loc, "format.day_short", ToDateTime(date));

    /// <summary>Week range title, e.g. "15 – 21 Tháng 11, 2026" / "Nov 15 – 21, 2026".</summary>
    public static string WeekRange(ILocalizationService loc, DateOnly start, DateOnly end) =>
        Format(
            loc,
            start.Month == end.Month && start.Year == end.Year ? "format.week_same_month" : "format.week_cross_month",
            ToDateTime(start),
            ToDateTime(end));

    /// <summary>Agenda group header, with "Hôm nay"/"Today" for the current day.</summary>
    public static string AgendaDay(ILocalizationService loc, DateOnly date, bool isToday) =>
        Format(loc, isToday ? "format.agenda_today" : "format.agenda_day", ToDateTime(date));

    /// <summary>Short weekday header for the month grid ("Th 2" / "Mon").</summary>
    public static string WeekdayShort(ILocalizationService loc, DayOfWeek day) =>
        Get(loc, "weekday.short." + _weekdayKeys[(int)day]);

    /// <summary>Minimal weekday header for the mini-calendar ("T2" / "Mo").</summary>
    public static string WeekdayMin(ILocalizationService loc, DayOfWeek day) =>
        Get(loc, "weekday.min." + _weekdayKeys[(int)day]);

    /// <summary>Leap-month marker appended to "1/month" (FR-UI-2): "n" in Vietnamese, "L" in English.</summary>
    public static string LeapMarker(ILocalizationService loc) => Get(loc, "lunar.leap_marker");

    /// <summary>
    /// Lunar text for a day cell (FR-UI-2): the first lunar day shows "1/month" plus the leap marker,
    /// any other day shows just the day number. Returns empty when <paramref name="lunar"/> is null.
    /// </summary>
    public static string LunarCell(LunarDate? lunar, string leapMarker)
    {
        if (lunar is not { } value)
        {
            return string.Empty;
        }

        return value.Day == 1
            ? string.Create(CultureInfo.InvariantCulture, $"1/{value.Month}{(value.IsLeapMonth ? leapMarker : string.Empty)}")
            : value.Day.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Lunar month name, e.g. "Tháng 6 (Nhuận)" / "Month 6 (Leap)".</summary>
    public static string LunarMonthName(ILocalizationService loc, int month, bool isLeap) =>
        Format(loc, isLeap ? "lunar.month_name_leap" : "lunar.month_name", month);

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static string Get(ILocalizationService loc, string key)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return loc.GetString(key, loc.CurrentCulture);
    }

    private static string Format(ILocalizationService loc, string key, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return string.Format(loc.CurrentCulture, loc.GetString(key, loc.CurrentCulture), args);
    }
}
