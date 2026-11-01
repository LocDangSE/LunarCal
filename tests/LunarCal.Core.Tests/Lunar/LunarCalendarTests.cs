using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>FR-LUNAR-1: conversion API, invariants over the whole supported range, and error handling.</summary>
public class LunarCalendarTests
{
    [Fact]
    public void FR_LUNAR_1_Every_supported_day_round_trips_and_advances_by_one_lunar_day()
    {
        var previous = LunarCalendar.ToLunar(LunarCalendar.MinSolarDate);
        LunarCalendar.ToSolar(previous).Should().Be(LunarCalendar.MinSolarDate);
        var problems = new List<string>();

        for (var day = LunarCalendar.MinSolarDate.AddDays(1); day <= LunarCalendar.MaxSolarDate; day = day.AddDays(1))
        {
            var lunar = LunarCalendar.ToLunar(day);
            if (LunarCalendar.ToSolar(lunar) != day)
            {
                problems.Add($"{day:yyyy-MM-dd} -> {lunar} does not round-trip");
            }

            var isNextDay = lunar == previous with { Day = previous.Day + 1 };
            var isNewMonth = lunar.Day == 1 && previous.Day is 29 or 30 && IsNextMonth(previous, lunar);
            if (!isNextDay && !isNewMonth)
            {
                problems.Add($"{day:yyyy-MM-dd}: {previous} -> {lunar}");
            }

            previous = lunar;
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void FR_LUNAR_1_Each_lunar_year_has_12_or_13_months_of_29_or_30_days()
    {
        for (var year = 1900; year <= 2099; year++)
        {
            var leap = LunarCalendar.GetLeapMonth(year);
            leap.Should().BeInRange(0, 12);

            var days = 0;
            for (var month = 1; month <= 12; month++)
            {
                var length = LunarCalendar.GetDaysInMonth(year, month, false);
                length.Should().BeOneOf(29, 30);
                days += length!.Value;

                var leapLength = LunarCalendar.GetDaysInMonth(year, month, true);
                if (month == leap)
                {
                    leapLength.Should().BeOneOf(29, 30);
                    days += leapLength!.Value;
                }
                else
                {
                    leapLength.Should().BeNull();
                }
            }

            days.Should().BeInRange(leap == 0 ? 353 : 383, leap == 0 ? 355 : 385, "lunar year {0}", year);
        }
    }

    [Fact]
    public void FR_LUNAR_1_Leap_month_11_of_2033_is_supported()
    {
        // The "2033 problem": the leap month falls after month 11, across the winter-solstice boundary.
        LunarCalendar.GetLeapMonth(2033).Should().Be(11);
        LunarCalendar.ToSolar(2033, 11, 1, true).Should().Be(new DateOnly(2033, 12, 22));
        LunarCalendar.ToLunar(new DateOnly(2034, 1, 20)).Should().Be(new LunarDate(2033, 12, 1, false));
    }

    [Fact]
    public void FR_LUNAR_1_Non_existent_lunar_dates_return_null()
    {
        LunarCalendar.ToSolar(2024, 5, 1, true).Should().BeNull("2024 has no leap month");
        LunarCalendar.ToSolar(2023, 3, 1, true).Should().BeNull("the 2023 leap month is 2");

        var shortMonth = Enumerable.Range(1, 12).First(m => LunarCalendar.GetDaysInMonth(2026, m, false) == 29);
        LunarCalendar.ToSolar(2026, shortMonth, 30, false).Should().BeNull("month {0} of 2026 has 29 days", shortMonth);
        LunarCalendar.ToSolar(2026, shortMonth, 29, false).Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void FR_LUNAR_1_ToSolar_rejects_an_invalid_day(int day)
    {
        var act = () => LunarCalendar.ToSolar(2026, 1, day, false);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(day));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void FR_LUNAR_1_Month_arguments_must_be_1_to_12(int month)
    {
        FluentActions.Invoking(() => LunarCalendar.ToSolar(2026, month, 1, false))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(month));
        FluentActions.Invoking(() => LunarCalendar.GetDaysInMonth(2026, month, false))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(month));
        FluentActions.Invoking(() => LunarCalendar.GetMonthCanChi(2026, month))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(month));
    }

    [Fact]
    public void FR_LUNAR_1_Range_is_1900_01_01_to_2100_12_31()
    {
        LunarCalendar.MinSolarDate.Should().Be(new DateOnly(1900, 1, 1));
        LunarCalendar.MaxSolarDate.Should().Be(new DateOnly(2100, 12, 31));

        var first = LunarCalendar.ToLunar(LunarCalendar.MinSolarDate);
        var last = LunarCalendar.ToLunar(LunarCalendar.MaxSolarDate);
        first.Year.Should().Be(LunarCalendar.MinLunarYear);
        last.Year.Should().Be(LunarCalendar.MaxLunarYear);
        LunarCalendar.GetTietKhi(LunarCalendar.MinSolarDate).Term.Should().Be(SolarTerm.DongChi);
        LunarCalendar.GetTietKhi(LunarCalendar.MaxSolarDate).Term.Should().Be(SolarTerm.DongChi);
    }

    [Fact]
    public void FR_LUNAR_1_Solar_dates_outside_the_range_throw_a_clear_error()
    {
        foreach (var date in new[] { new DateOnly(1899, 12, 31), new DateOnly(2101, 1, 1), DateOnly.MinValue, DateOnly.MaxValue })
        {
            FluentActions.Invoking(() => LunarCalendar.ToLunar(date))
                .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("solar").WithMessage("*1900-01-01..2100-12-31*");
            FluentActions.Invoking(() => LunarCalendar.GetTietKhi(date))
                .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("solar");
        }
    }

    [Theory]
    [InlineData(1898)]
    [InlineData(2101)]
    public void FR_LUNAR_1_Lunar_years_outside_the_range_throw_a_clear_error(int year)
    {
        FluentActions.Invoking(() => LunarCalendar.ToSolar(year, 1, 1, false))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lunarYear").WithMessage("*1899..2100*");
        FluentActions.Invoking(() => LunarCalendar.GetLeapMonth(year))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lunarYear");
        FluentActions.Invoking(() => LunarCalendar.GetDaysInMonth(year, 1, false))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lunarYear");
    }

    [Fact]
    public void FR_LUNAR_1_Lunar_dates_whose_solar_date_is_outside_the_range_throw()
    {
        // Lunar 1899 month 1 is in early 1899; lunar 2100 month 12 ends in 2101.
        FluentActions.Invoking(() => LunarCalendar.ToSolar(1899, 1, 1, false))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*outside the supported range*");

        var lastLunar = LunarCalendar.ToLunar(LunarCalendar.MaxSolarDate);
        var lastDay = LunarCalendar.GetDaysInMonth(lastLunar.Year, lastLunar.Month, lastLunar.IsLeapMonth)!.Value;
        FluentActions.Invoking(() => LunarCalendar.ToSolar(lastLunar with { Day = lastDay }))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*outside the supported range*");
    }

    [Fact]
    public void LunarDate_has_an_invariant_debug_format()
    {
        new LunarDate(2023, 2, 1, true).ToString().Should().Be("2023-02L-01");
        new LunarDate(2026, 9, 15, false).ToString().Should().Be("2026-09-15");
    }

    private static bool IsNextMonth(LunarDate previous, LunarDate next)
    {
        var leap = LunarCalendar.GetLeapMonth(previous.Year);
        if (!previous.IsLeapMonth && previous.Month == leap)
        {
            // Regular month followed by its leap month.
            return next.Year == previous.Year && next.Month == previous.Month && next.IsLeapMonth;
        }

        return previous.Month == 12
            ? next is { Year: var y, Month: 1, IsLeapMonth: false } && y == previous.Year + 1
            : next.Year == previous.Year && next.Month == previous.Month + 1 && !next.IsLeapMonth;
    }
}
