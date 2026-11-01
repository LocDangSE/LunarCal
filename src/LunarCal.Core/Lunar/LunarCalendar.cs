using System.Globalization;

namespace LunarCal.Core.Lunar;

/// <summary>
/// Vietnamese lunar calendar (FR-LUNAR-1..3): Hồ Ngọc Đức's algorithm (new moon and major solar
/// terms) with the time zone fixed at UTC+7. Offline, pure C#, thread-safe.
/// Supported range: solar dates <see cref="MinSolarDate"/>–<see cref="MaxSolarDate"/> (ADR-0006).
/// </summary>
public static class LunarCalendar
{
    /// <summary>First supported solar date.</summary>
    public static readonly DateOnly MinSolarDate = new(1900, 1, 1);

    /// <summary>Last supported solar date.</summary>
    public static readonly DateOnly MaxSolarDate = new(2100, 12, 31);

    /// <summary>Lowest lunar year accepted by lunar-to-solar methods (1900-01-01 is in lunar year 1899).</summary>
    public const int MinLunarYear = 1899;

    /// <summary>Highest lunar year accepted by lunar-to-solar methods.</summary>
    public const int MaxLunarYear = 2100;

    /// <summary>Converts a solar (Gregorian) date to a lunar date.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="solar"/> is outside the supported range.</exception>
    public static LunarDate ToLunar(DateOnly solar)
    {
        EnsureInRange(solar, nameof(solar));
        var month = FindMonth(Astronomy.ToJdn(solar));
        return new LunarDate(month.Info.LunarYear, month.Info.Month, month.Day, month.Info.IsLeap);
    }

    /// <summary>
    /// Converts a lunar date to a solar date. Returns <see langword="null"/> when the date does not exist
    /// in that year: the month is not leap, or day 30 is requested in a 29-day month.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="month"/> is not 1–12, <paramref name="day"/> is not 1–30, or the date is outside the supported range.
    /// </exception>
    public static DateOnly? ToSolar(int lunarYear, int month, int day, bool isLeap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 30);

        if (FindLunarMonth(lunarYear, month, isLeap) is not { } info || day > info.Length)
        {
            return null;
        }

        var solar = Astronomy.FromJdn(info.StartJdn + day - 1);
        if (solar < MinSolarDate || solar > MaxSolarDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lunarYear),
                string.Create(CultureInfo.InvariantCulture, $"Lunar date {new LunarDate(lunarYear, month, day, isLeap)} is {solar:yyyy-MM-dd}, outside the supported range {MinSolarDate:yyyy-MM-dd}..{MaxSolarDate:yyyy-MM-dd}."));
        }

        return solar;
    }

    /// <inheritdoc cref="ToSolar(int, int, int, bool)"/>
    public static DateOnly? ToSolar(LunarDate date) => ToSolar(date.Year, date.Month, date.Day, date.IsLeapMonth);

    /// <summary>Leap month number of a lunar year (1–12), or 0 if the year has no leap month.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lunarYear"/> is outside the supported range.</exception>
    public static int GetLeapMonth(int lunarYear)
    {
        EnsureLunarYear(lunarYear);

        // A year's leap month is either in its own suì (months 1–10) or in the next one (months 11–12).
        foreach (var sui in (ReadOnlySpan<int>)[lunarYear, lunarYear + 1])
        {
            foreach (var m in LunarYearTable.Get(sui).Months)
            {
                if (m.IsLeap && m.LunarYear == lunarYear)
                {
                    return m.Month;
                }
            }
        }

        return 0;
    }

    /// <summary>Length (29 or 30 days) of a lunar month, or <see langword="null"/> if that (leap) month does not exist.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lunarYear"/> or <paramref name="month"/> is out of range.</exception>
    public static int? GetDaysInMonth(int lunarYear, int month, bool isLeap) => FindLunarMonth(lunarYear, month, isLeap)?.Length;

    /// <summary>Can Chi of a day. The cycle is continuous and independent of the time zone.</summary>
    public static CanChi GetDayCanChi(DateOnly solar)
    {
        var jdn = Astronomy.ToJdn(solar);
        return new CanChi((ThienCan)((jdn + 9) % 10), (DiaChi)((jdn + 1) % 12));
    }

    /// <summary>
    /// Can Chi of a lunar month. Month 1 is always Dần; the stem follows the year's stem.
    /// A leap month shares the Can Chi of the regular month with the same number (ADR-0006).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="month"/> is not 1–12.</exception>
    public static CanChi GetMonthCanChi(int lunarYear, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        return new CanChi((ThienCan)Mod((lunarYear * 12) + month + 3, 10), (DiaChi)((month + 1) % 12));
    }

    /// <summary>Can Chi of the lunar month that contains <paramref name="date"/>.</summary>
    public static CanChi GetMonthCanChi(LunarDate date) => GetMonthCanChi(date.Year, date.Month);

    /// <summary>Can Chi of a lunar year, e.g. 2026 → Bính Ngọ.</summary>
    public static CanChi GetYearCanChi(int lunarYear) =>
        new((ThienCan)Mod(lunarYear + 6, 10), (DiaChi)Mod(lunarYear + 8, 12));

    /// <summary>Solar term in effect on a day (UTC+7), and whether the term starts on that day.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="solar"/> is outside the supported range.</exception>
    public static TietKhi GetTietKhi(DateOnly solar)
    {
        EnsureInRange(solar, nameof(solar));
        var jdn = Astronomy.ToJdn(solar);

        // Term at the end of the day (local midnight that starts the next day), so the day on which
        // the sun reaches a term's longitude carries that term.
        var term = Astronomy.SunLongitudeSector(jdn + 1, 24);
        var previous = Astronomy.SunLongitudeSector(jdn, 24);
        return new TietKhi((SolarTerm)term, term != previous);
    }

    private static (LunarMonthInfo Info, int Day) FindMonth(int jdn)
    {
        var year = Astronomy.FromJdn(jdn).Year;
        var table = LunarYearTable.Get(year);
        if (jdn >= table.EndJdn)
        {
            table = LunarYearTable.Get(year + 1);
        }

        var months = table.Months;
        for (var i = months.Count - 1; i > 0; i--)
        {
            if (jdn >= months[i].StartJdn)
            {
                return (months[i], jdn - months[i].StartJdn + 1);
            }
        }

        return (months[0], jdn - months[0].StartJdn + 1);
    }

    private static LunarMonthInfo? FindLunarMonth(int lunarYear, int month, bool isLeap)
    {
        EnsureLunarYear(lunarYear);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        // Months 11 and 12 (and a leap 11 or 12) come after the winter solstice, in the next suì.
        var table = LunarYearTable.Get(month >= 11 ? lunarYear + 1 : lunarYear);
        foreach (var m in table.Months)
        {
            if (m.LunarYear == lunarYear && m.Month == month && m.IsLeap == isLeap)
            {
                return m;
            }
        }

        return null;
    }

    private static void EnsureInRange(DateOnly solar, string paramName)
    {
        if (solar < MinSolarDate || solar > MaxSolarDate)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                solar,
                string.Create(CultureInfo.InvariantCulture, $"The lunar calendar supports solar dates {MinSolarDate:yyyy-MM-dd}..{MaxSolarDate:yyyy-MM-dd}."));
        }
    }

    private static void EnsureLunarYear(int lunarYear)
    {
        if (lunarYear is < MinLunarYear or > MaxLunarYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lunarYear),
                lunarYear,
                string.Create(CultureInfo.InvariantCulture, $"The lunar calendar supports lunar years {MinLunarYear}..{MaxLunarYear}."));
        }
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
