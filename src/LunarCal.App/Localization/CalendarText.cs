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

    /// <summary>
    /// Day title of the top bar in Day view (Figma S07): "Thứ 6, 16 Tháng 10, 2026" / "Fri, October 16, 2026".
    /// </summary>
    public static string DayTitle(ILocalizationService loc, DateOnly date) =>
        Format(loc, "format.day_title", WeekdayShort(loc, date.DayOfWeek), ToDateTime(date));

    /// <summary>
    /// Lunar subtitle for a range of solar dates shown as months (Month and Agenda views, Figma 01, S08):
    /// "Âm lịch: tháng 8 – 9 năm Bính Ngọ", or one month, or a range across two lunar years (FR-UI-9).
    /// </summary>
    public static string LunarMonthHeader(ILocalizationService loc, DateOnly first, DateOnly last)
    {
        if (!TryLunar(first, out var a) || !TryLunar(last, out var b))
        {
            return string.Empty;
        }

        if (a.Year != b.Year)
        {
            return Format(
                loc,
                "lunar.header_month_range_years",
                MonthLabel(loc, a),
                LunarCalendar.GetYearCanChi(a.Year),
                MonthLabel(loc, b),
                LunarCalendar.GetYearCanChi(b.Year));
        }

        var year = LunarCalendar.GetYearCanChi(a.Year);
        return a.Month == b.Month && a.IsLeapMonth == b.IsLeapMonth
            ? Format(loc, "lunar.header_month", MonthLabel(loc, a), year)
            : Format(loc, "lunar.header_month_range", MonthLabel(loc, a), MonthLabel(loc, b), year);
    }

    /// <summary>
    /// Lunar subtitle for a week (Figma S06): "Âm lịch: 3 – 9 tháng 9 năm Bính Ngọ", or "28/8 – 4/9" when the
    /// week crosses a lunar month (FR-UI-9).
    /// </summary>
    public static string LunarWeekHeader(ILocalizationService loc, DateOnly start, DateOnly end)
    {
        if (!TryLunar(start, out var a) || !TryLunar(end, out var b))
        {
            return string.Empty;
        }

        var year = LunarCalendar.GetYearCanChi(b.Year);
        return a.Year == b.Year && a.Month == b.Month && a.IsLeapMonth == b.IsLeapMonth
            ? Format(loc, "lunar.header_week", a.Day, b.Day, MonthLabel(loc, a), year)
            : Format(loc, "lunar.header_week_cross", a.Day, MonthLabel(loc, a), b.Day, MonthLabel(loc, b), year);
    }

    /// <summary>
    /// Lunar subtitle for one day (Figma S07): "Âm lịch: 7 tháng 9 năm Bính Ngọ · Quý Hợi" (FR-UI-9).
    /// </summary>
    public static string LunarDayHeader(ILocalizationService loc, DateOnly date) =>
        TryLunar(date, out var l)
            ? Format(loc, "lunar.header_day", l.Day, MonthLabel(loc, l), LunarCalendar.GetYearCanChi(l.Year), LunarCalendar.GetDayCanChi(date))
            : string.Empty;

    /// <summary>
    /// Lunar date in words (Figma S07 column header, S09 lunar block): "Mùng 7 tháng 9" for days 1–10,
    /// "15 tháng 9" afterwards; "Lunar 7/9" in English.
    /// </summary>
    public static string LunarDayMonth(ILocalizationService loc, LunarDate lunar) =>
        Format(loc, lunar.Day <= 10 ? "lunar.day_month_mung" : "lunar.day_month", lunar.Day, MonthLabel(loc, lunar));

    /// <summary>UTC offset label of the hour gutter (Figma S06): "GMT+7", "GMT-5", "GMT+5:30".</summary>
    public static string GmtOffset(ILocalizationService loc, TimeZoneInfo zone, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var offset = zone.GetUtcOffset(at);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var value = abs.Minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours}")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours}:{abs.Minutes:D2}");
        return Format(loc, "week.gmt", value);
    }

    /// <summary>Lunar month number with the localized leap suffix ("6 nhuận" / "6 (leap)").</summary>
    public static string MonthLabel(ILocalizationService loc, LunarDate lunar) =>
        lunar.IsLeapMonth
            ? Format(loc, "lunar.month_label_leap", lunar.Month)
            : lunar.Month.ToString(loc.CurrentCulture);

    private static bool TryLunar(DateOnly date, out LunarDate lunar)
    {
        if (date >= LunarCalendar.MinSolarDate && date <= LunarCalendar.MaxSolarDate)
        {
            lunar = LunarCalendar.ToLunar(date);
            return true;
        }

        lunar = default;
        return false;
    }

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
