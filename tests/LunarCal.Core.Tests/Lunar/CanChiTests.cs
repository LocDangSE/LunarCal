using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>FR-LUNAR-1: Can Chi of day, month and year.</summary>
public class CanChiTests
{
    [Theory]
    [InlineData(2023, "Quý Mão")]
    [InlineData(2024, "Giáp Thìn")]
    [InlineData(2025, "Ất Tỵ")]
    [InlineData(2026, "Bính Ngọ")]
    [InlineData(1984, "Giáp Tý")] // start of the current 60-year cycle
    [InlineData(1900, "Canh Tý")]
    [InlineData(2100, "Canh Thân")]
    public void FR_LUNAR_1_Year_CanChi(int year, string expected) =>
        LunarCalendar.GetYearCanChi(year).ToString().Should().Be(expected);

    [Fact]
    public void FR_LUNAR_1_Year_CanChi_repeats_every_60_years()
    {
        for (var year = 1900; year <= 2100; year++)
        {
            LunarCalendar.GetYearCanChi(year).CycleIndex.Should().Be((year - 1984 + 600) % 60);
        }
    }

    [Theory]
    [InlineData(2000, 1, 1, "Mậu Ngọ")]
    [InlineData(2024, 2, 10, "Giáp Thìn")] // Tết Giáp Thìn
    public void FR_LUNAR_1_Day_CanChi(int y, int m, int d, string expected) =>
        LunarCalendar.GetDayCanChi(new DateOnly(y, m, d)).ToString().Should().Be(expected);

    [Fact]
    public void FR_LUNAR_1_Day_CanChi_advances_by_one_every_day()
    {
        var previous = LunarCalendar.GetDayCanChi(LunarCalendar.MinSolarDate).CycleIndex;
        for (var day = LunarCalendar.MinSolarDate.AddDays(1); day <= LunarCalendar.MaxSolarDate; day = day.AddDays(1))
        {
            var current = LunarCalendar.GetDayCanChi(day).CycleIndex;
            current.Should().Be((previous + 1) % 60);
            previous = current;
        }
    }

    [Theory] // Ngũ hổ độn: the stem of month 1 (Dần) follows the year's stem.
    [InlineData(2024, "Bính Dần")] // Giáp
    [InlineData(2025, "Mậu Dần")] // Ất
    [InlineData(2026, "Canh Dần")] // Bính
    [InlineData(2027, "Nhâm Dần")] // Đinh
    [InlineData(2028, "Giáp Dần")] // Mậu
    [InlineData(2029, "Bính Dần")] // Kỷ
    [InlineData(2023, "Giáp Dần")] // Quý
    public void FR_LUNAR_1_Month_1_CanChi_follows_the_year_stem(int year, string expected) =>
        LunarCalendar.GetMonthCanChi(year, 1).ToString().Should().Be(expected);

    [Fact]
    public void FR_LUNAR_1_Month_CanChi_continues_across_years()
    {
        // Month chi is fixed (month 1 = Dần … month 11 = Tý, month 12 = Sửu); the stem advances by one per month,
        // so month 12 of one year and month 1 of the next are consecutive in the 60-term cycle.
        for (var year = 1900; year < 2100; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                var canChi = LunarCalendar.GetMonthCanChi(year, month);
                canChi.Chi.Should().Be((DiaChi)((month + 1) % 12));
                var next = month == 12 ? LunarCalendar.GetMonthCanChi(year + 1, 1) : LunarCalendar.GetMonthCanChi(year, month + 1);
                next.CycleIndex.Should().Be((canChi.CycleIndex + 1) % 60);
            }
        }
    }

    [Fact]
    public void FR_LUNAR_1_Leap_month_shares_the_CanChi_of_the_regular_month()
    {
        var leap = new LunarDate(2023, 2, 10, true);
        LunarCalendar.GetMonthCanChi(leap).Should().Be(LunarCalendar.GetMonthCanChi(2023, 2));
        LunarCalendar.GetMonthCanChi(leap).ToString().Should().Be("Ất Mão");
    }

    [Fact]
    public void CanChi_cycle_index_round_trips()
    {
        for (var i = 0; i < 60; i++)
        {
            CanChi.FromCycleIndex(i).CycleIndex.Should().Be(i);
        }

        CanChi.FromCycleIndex(60).Should().Be(CanChi.FromCycleIndex(0));
        CanChi.FromCycleIndex(-1).ToString().Should().Be("Quý Hợi");
        new CanChi(ThienCan.Giap, DiaChi.Suu).CycleIndex.Should().Be(-1, "stems and branches of different parity never pair");
    }

    [Fact]
    public void CanChi_exposes_Vietnamese_names()
    {
        var canChi = new CanChi(ThienCan.Dinh, DiaChi.Ti);
        canChi.CanName.Should().Be("Đinh");
        canChi.ChiName.Should().Be("Tỵ");
        canChi.ToString().Should().Be("Đinh Tỵ");
    }
}
