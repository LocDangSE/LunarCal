using System.Diagnostics;
using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>FR-LUNAR-2: computing a whole month takes &lt; 5 ms, thanks to the per-year cache.</summary>
public class LunarPerformanceTests
{
    [Fact]
    public void FR_LUNAR_2_A_month_grid_is_computed_in_under_5_ms()
    {
        // Warm the per-year cache the way the app does on first display.
        _ = ComputeMonthGrid(2026, 10);

        var worst = TimeSpan.Zero;
        for (var month = 1; month <= 12; month++)
        {
            var stopwatch = Stopwatch.StartNew();
            ComputeMonthGrid(2026, month).Should().HaveCount(42);
            stopwatch.Stop();
            worst = stopwatch.Elapsed > worst ? stopwatch.Elapsed : worst;
        }

        worst.Should().BeLessThan(TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void FR_LUNAR_2_A_cold_month_is_computed_in_under_5_ms()
    {
        // A year not used by other tests in this process, so its tables are not cached yet (first call
        // still pays the JIT; measure the second distinct cold year).
        _ = ComputeMonthGrid(1950, 6);
        var stopwatch = Stopwatch.StartNew();
        ComputeMonthGrid(1951, 6).Should().HaveCount(42);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void FR_LUNAR_2_Year_tables_are_cached()
    {
        _ = LunarCalendar.ToLunar(new DateOnly(2040, 5, 5));
        var count = LunarYearTable.CachedCount;
        _ = LunarCalendar.ToLunar(new DateOnly(2040, 5, 6));
        LunarYearTable.CachedCount.Should().Be(count);
        LunarYearTable.Get(2040).Should().BeSameAs(LunarYearTable.Get(2040));
    }

    /// <summary>The 6×7 month grid: lunar date, day Can Chi and solar term for each cell.</summary>
    private static List<(LunarDate Lunar, CanChi Day, TietKhi Term)> ComputeMonthGrid(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); // Monday-first grid
        return
        [
            .. Enumerable.Range(0, 42)
                .Select(start.AddDays)
                .Select(d => (LunarCalendar.ToLunar(d), LunarCalendar.GetDayCanChi(d), LunarCalendar.GetTietKhi(d))),
        ];
    }
}
