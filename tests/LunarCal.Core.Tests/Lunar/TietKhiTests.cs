using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>FR-LUNAR-1: solar terms (tiết khí). Full-range reference check is in <see cref="LunarReferenceTests"/>.</summary>
public class TietKhiTests
{
    [Theory] // HKO 2026 start days that are not near local midnight
    [InlineData(2026, 2, 4, SolarTerm.LapXuan)]
    [InlineData(2026, 3, 20, SolarTerm.XuanPhan)]
    [InlineData(2026, 6, 21, SolarTerm.HaChi)]
    [InlineData(2026, 12, 22, SolarTerm.DongChi)]
    public void FR_LUNAR_1_Term_starts_on_the_reference_day(int y, int m, int d, SolarTerm term)
    {
        var day = new DateOnly(y, m, d);
        LunarCalendar.GetTietKhi(day).Should().Be(new TietKhi(term, true));
        LunarCalendar.GetTietKhi(day.AddDays(-1)).Term.Should().Be((SolarTerm)(((int)term + 23) % 24));
        LunarCalendar.GetTietKhi(day.AddDays(1)).Should().Be(new TietKhi(term, false));
    }

    [Fact]
    public void FR_LUNAR_1_Each_year_has_24_terms_in_order()
    {
        for (var year = 1900; year <= 2100; year++)
        {
            var starts = new List<SolarTerm>();
            for (var day = new DateOnly(year, 1, 1); day.Year == year; day = day.AddDays(1))
            {
                var tietKhi = LunarCalendar.GetTietKhi(day);
                if (tietKhi.IsStartDay)
                {
                    starts.Add(tietKhi.Term);
                }
            }

            // Every year starts in Đông chí and has Tiểu hàn (early January) … Đông chí (late December).
            starts.Should().HaveCount(24);
            starts.Should().Equal(Enumerable.Range(0, 24).Select(i => (SolarTerm)((i + 19) % 24)));
        }
    }

    [Theory]
    [InlineData(SolarTerm.XuanPhan, 0, "Xuân phân")]
    [InlineData(SolarTerm.LapDong, 225, "Lập đông")]
    [InlineData(SolarTerm.DongChi, 270, "Đông chí")]
    [InlineData(SolarTerm.KinhTrap, 345, "Kinh trập")]
    public void TietKhi_exposes_longitude_and_Vietnamese_name(SolarTerm term, int degrees, string name)
    {
        var tietKhi = new TietKhi(term, false);
        tietKhi.LongitudeDegrees.Should().Be(degrees);
        tietKhi.VietnameseName.Should().Be(name);
        tietKhi.ToString().Should().Be(name);
    }

    [Fact]
    public void TietKhi_has_a_name_for_every_term()
    {
        Enum.GetValues<SolarTerm>().Should().HaveCount(24);
        Enum.GetValues<SolarTerm>().Select(t => new TietKhi(t, false).VietnameseName).Should().OnlyHaveUniqueItems();
    }
}
