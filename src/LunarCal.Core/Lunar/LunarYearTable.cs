using System.Collections.Concurrent;

namespace LunarCal.Core.Lunar;

/// <summary>One lunar month: its first day (JDN), number, leap flag and lunar year.</summary>
internal readonly record struct LunarMonthInfo(int StartJdn, int LunarYear, int Month, bool IsLeap, int Length);

/// <summary>
/// The lunar months of one "suì": from the start of month 11 of Gregorian year <c>Y − 1</c>
/// (inclusive) to the start of month 11 of year <c>Y</c> (exclusive). This is the unit in which
/// Hồ Ngọc Đức's algorithm decides the leap month. Tables are computed once and cached per year
/// (FR-LUNAR-2).
/// </summary>
internal sealed class LunarYearTable
{
    private static readonly ConcurrentDictionary<int, LunarYearTable> _cache = new();

    private LunarYearTable(int year, LunarMonthInfo[] months)
    {
        Year = year;
        Months = months;
    }

    /// <summary>Gregorian year <c>Y</c> whose winter solstice ends this suì.</summary>
    public int Year { get; }

    /// <summary>12 or 13 months, in order.</summary>
    public IReadOnlyList<LunarMonthInfo> Months { get; }

    /// <summary>JDN of the first day after this table (start of month 11 of year <see cref="Year"/>).</summary>
    public int EndJdn => Months[^1].StartJdn + Months[^1].Length;

    public static LunarYearTable Get(int year) => _cache.GetOrAdd(year, Compute);

    /// <summary>Number of cached tables (test hook for the cache behaviour).</summary>
    internal static int CachedCount => _cache.Count;

    private static LunarYearTable Compute(int year)
    {
        var a11 = Astronomy.LunarMonth11Start(year - 1);
        var b11 = Astronomy.LunarMonth11Start(year);
        var k0 = Astronomy.NewMoonIndexNear(a11);

        // A suì has 13 lunations (a leap year) when two month-11 starts are more than 365 days apart.
        var isLeapSui = b11 - a11 > 365;
        var count = isLeapSui ? 13 : 12;
        var leapOffset = isLeapSui ? Astronomy.LeapMonthOffset(a11) : -1;

        var starts = new int[count + 1];
        starts[0] = a11;
        for (var i = 1; i < count; i++)
        {
            starts[i] = Astronomy.NewMoonDay(k0 + i);
        }

        starts[count] = b11;

        var months = new LunarMonthInfo[count];
        for (var i = 0; i < count; i++)
        {
            // Without a leap month, index 0 is month 11, index 1 is month 12, index 2 is month 1, ...
            // From the leap month on, numbering is shifted back by one; the leap month repeats the previous number.
            var shift = isLeapSui && i >= leapOffset ? 1 : 0;
            var month = ((11 + i - shift - 1) % 12) + 1;
            var isLeap = isLeapSui && i == leapOffset;

            // Months 11 and 12 at the start of the suì still belong to the previous lunar year.
            var lunarYear = month >= 11 && i < 4 ? year - 1 : year;

            months[i] = new LunarMonthInfo(starts[i], lunarYear, month, isLeap, starts[i + 1] - starts[i]);
        }

        return new LunarYearTable(year, months);
    }
}
