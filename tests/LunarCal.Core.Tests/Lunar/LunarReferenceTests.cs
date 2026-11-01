using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>
/// FR-LUNAR-3: the engine matches published reference data. Fixed points from the spec, Wikipedia,
/// and the full Hong Kong Observatory (HKO) table 1901–2100 (2,474 month starts, 4,800 solar terms).
/// HKO uses UTC+8 and the Vietnamese calendar UTC+7, so every disagreement must be explained by
/// that one-hour offset (see Fixtures/README.md).
/// </summary>
public class LunarReferenceTests
{
    // UT hour window in which an instant falls on different civil days in UTC+7 and UTC+8:
    // 16:00 UT = 23:00 (UTC+7) = 00:00 (UTC+8) next day; 17:00 UT = 00:00 (UTC+7) next day.
    private const double OffsetWindowStartUt = 16.0;
    private const double OffsetWindowEndUt = 17.0;

    // Hồ Ngọc Đức's sun longitude series is accurate to about 0.01° (~15 min); the worst
    // observed deviation against HKO in 1901–2100 is 24 min. Allow 30 min.
    private const double SunLongitudeToleranceHours = 0.5;

    [Theory] // FR-LUNAR-3: Tết dates required by the spec
    [InlineData(2023, 1, 22)] // Quý Mão
    [InlineData(2024, 2, 10)] // Giáp Thìn
    [InlineData(2025, 1, 29)] // Ất Tỵ
    [InlineData(2026, 2, 17)] // Bính Ngọ
    public void FR_LUNAR_3_Tet_falls_on_the_spec_date(int year, int month, int day)
    {
        var tet = new DateOnly(year, month, day);

        LunarCalendar.ToLunar(tet).Should().Be(new LunarDate(year, 1, 1, false));
        LunarCalendar.ToLunar(tet.AddDays(-1)).Should().Match<LunarDate>(d => d.Year == year - 1 && d.Month == 12);
        LunarCalendar.ToSolar(year, 1, 1, false).Should().Be(tet);
    }

    [Theory] // FR-LUNAR-3: leap months required by the spec
    [InlineData(2023, 2)]
    [InlineData(2025, 6)]
    [InlineData(2024, 0)]
    [InlineData(2026, 0)]
    public void FR_LUNAR_3_Leap_month_matches_the_spec(int lunarYear, int expectedLeapMonth) =>
        LunarCalendar.GetLeapMonth(lunarYear).Should().Be(expectedLeapMonth);

    [Fact] // FR-LUNAR-3: leap month 2 of 2023 starts 2023-03-22 (HKO) and lasts 29 days
    public void FR_LUNAR_3_Leap_month_2_of_2023_is_placed_after_month_2()
    {
        LunarCalendar.ToSolar(2023, 2, 1, true).Should().Be(new DateOnly(2023, 3, 22));
        LunarCalendar.ToLunar(new DateOnly(2023, 3, 21)).Should().Be(new LunarDate(2023, 2, 30, false));
        LunarCalendar.GetDaysInMonth(2023, 2, true).Should().Be(29);
    }

    [Theory] // FR-LUNAR-3: Vietnamese dates cross-checked with Wikipedia (see Fixtures/README.md)
    [InlineData(1968, 1, 29, 1968, 1, 1, false)] // Tết Mậu Thân in North Vietnam (UTC+7); South Vietnam (UTC+8) on 30/1
    [InlineData(1985, 1, 21, 1985, 1, 1, false)] // Tết 1985, one month before China's (20/2/1985)
    [InlineData(1984, 11, 23, 1984, 11, 1, false)] // Vietnamese month 11 of 1984
    [InlineData(1985, 3, 21, 1985, 2, 1, true)] // Leap month 21/3 – 19/4/1985
    [InlineData(1985, 4, 19, 1985, 2, 30, true)]
    [InlineData(2027, 2, 6, 2027, 1, 1, false)] // Tết Đinh Mùi
    public void FR_LUNAR_3_Matches_documented_Vietnamese_dates(int y, int m, int d, int ly, int lm, int ld, bool leap) =>
        LunarCalendar.ToLunar(new DateOnly(y, m, d)).Should().Be(new LunarDate(ly, lm, ld, leap));

    [Fact] // FR-LUNAR-3: ≥ 200 reference points (here 2,474 month starts)
    public void FR_LUNAR_3_Every_HKO_month_start_matches_or_differs_only_by_the_time_zone()
    {
        var leapDifferences = Fixtures.LeapDifferences.ToDictionary(r => r.Date, r => r.Lunar);
        var exact = 0;
        var oneDayEarlier = 0;
        var problems = new List<string>();

        foreach (var (date, hko) in Fixtures.HkoMonthStarts)
        {
            var vn = LunarCalendar.ToLunar(date);
            if (leapDifferences.TryGetValue(date, out var expectedVn))
            {
                if (vn != expectedVn)
                {
                    problems.Add($"{date:yyyy-MM-dd}: expected {expectedVn} (leap difference), got {vn}");
                }

                continue;
            }

            if (vn == hko)
            {
                exact++;
                continue;
            }

            // Only allowed difference: the new moon falls in 16:00–17:00 UT, i.e. before midnight in
            // UTC+7 but after midnight in UTC+8, so the Vietnamese month starts one day earlier.
            var vnStart = date.AddDays(-1);
            var hour = NewMoonUtHour(vnStart);
            if (vn == hko with { Day = 2 }
                && LunarCalendar.ToLunar(vnStart) == hko
                && hour >= OffsetWindowStartUt && hour < OffsetWindowEndUt)
            {
                oneDayEarlier++;
            }
            else
            {
                problems.Add($"{date:yyyy-MM-dd}: HKO {hko}, VN {vn}, new moon at {hour:F2} UT");
            }
        }

        problems.Should().BeEmpty();
        Fixtures.HkoMonthStarts.Should().HaveCount(2474);
        exact.Should().Be(2384);
        oneDayEarlier.Should().Be(77);
        leapDifferences.Should().HaveCount(13);
    }

    [Fact] // FR-LUNAR-3: Tết 1901–2100 (200 reference points) agrees with HKO except for time-zone shifts
    public void FR_LUNAR_3_Tet_every_year_matches_HKO_or_is_one_day_earlier()
    {
        var tetByYear = Fixtures.HkoMonthStarts
            .Where(r => r.Lunar is { Month: 1, IsLeapMonth: false } && r.Date.Year >= 1901)
            .ToDictionary(r => r.Lunar.Year, r => r.Date);
        tetByYear.Should().HaveCount(200);

        var shifted = new List<int>();
        foreach (var (year, hkoTet) in tetByYear)
        {
            var vnTet = LunarCalendar.ToSolar(year, 1, 1, false)!.Value;
            if (vnTet != hkoTet)
            {
                shifted.Add(year);
            }
        }

        // 1985 is the leap-month case (a month earlier); the rest are one-day shifts.
        shifted.Should().Equal(
            1903, 1935, 1965, 1968, 1969, 1985, 2007, 2030, 2053);
        LunarCalendar.ToSolar(2007, 1, 1, false).Should().Be(new DateOnly(2007, 2, 17));
    }

    [Fact] // FR-LUNAR-1, FR-LUNAR-3: 4,800 solar terms 1901–2100
    public void FR_LUNAR_3_Every_HKO_solar_term_matches_or_differs_only_by_the_time_zone()
    {
        var exact = 0;
        var oneDayEarlier = 0;
        var problems = new List<string>();

        foreach (var (date, term) in Fixtures.HkoSolarTerms)
        {
            if (LunarCalendar.GetTietKhi(date) == new TietKhi(term, true))
            {
                exact++;
                continue;
            }

            // Allowed difference: the term starts one day earlier in Vietnam because the sun reaches
            // the term's longitude in 16:00–17:00 UT (± the precision of the sun longitude series).
            var vnStart = date.AddDays(-1);
            var hour = TermUtHour(vnStart, term);
            if (LunarCalendar.GetTietKhi(vnStart) == new TietKhi(term, true)
                && LunarCalendar.GetTietKhi(date) == new TietKhi(term, false)
                && hour >= OffsetWindowStartUt - SunLongitudeToleranceHours && hour < OffsetWindowEndUt)
            {
                oneDayEarlier++;
            }
            else
            {
                problems.Add($"{date:yyyy-MM-dd} {term}: VN {LunarCalendar.GetTietKhi(date)}, crossing at {hour:F2} UT");
            }
        }

        problems.Should().BeEmpty();
        Fixtures.HkoSolarTerms.Should().HaveCount(4800);
        exact.Should().Be(4561);
        oneDayEarlier.Should().Be(239);
    }

    [Fact] // FR-LUNAR-1: every HKO leap year is also a 13-month year in Vietnam (possibly a different month)
    public void FR_LUNAR_1_Leap_years_match_HKO_except_1984_and_1985()
    {
        var hkoLeapYears = Fixtures.HkoMonthStarts.Where(r => r.Lunar.IsLeapMonth).Select(r => r.Lunar.Year).ToHashSet();
        var vnLeapYears = Enumerable.Range(1901, 199).Where(y => LunarCalendar.GetLeapMonth(y) != 0).ToHashSet();

        hkoLeapYears.Except(vnLeapYears).Should().Equal(1984);
        vnLeapYears.Except(hkoLeapYears).Should().Equal(1985);
    }

    /// <summary>UT hour (0–24) of the new moon whose Vietnamese civil day is <paramref name="day"/>.</summary>
    private static double NewMoonUtHour(DateOnly day)
    {
        var k = Astronomy.NewMoonIndexNear(Astronomy.ToJdn(day));
        var jd = Astronomy.NewMoon(k);
        return (jd + 0.5 - Math.Floor(jd + 0.5)) * 24;
    }

    /// <summary>UT hour at which the sun reaches <paramref name="term"/> during Vietnamese civil day <paramref name="day"/>.</summary>
    private static double TermUtHour(DateOnly day, SolarTerm term)
    {
        var target = (int)term * Math.PI / 12;
        var lo = Astronomy.ToJdn(day) - 0.5 - (Astronomy.TimeZoneHours / 24);
        var hi = lo + 1;
        for (var i = 0; i < 50; i++)
        {
            var mid = (lo + hi) / 2;
            var diff = Math.IEEERemainder(Astronomy.SunLongitude(mid) - target, 2 * Math.PI);
            if (diff < 0)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + 0.5 - Math.Floor(lo + 0.5)) * 24;
    }
}
