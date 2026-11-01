using System.Globalization;
using AwesomeAssertions;
using LunarCal.App.Localization;
using Xunit;

namespace LunarCal.App.Tests.Localization;

public sealed class TimeFormattingHelperTests
{
    private readonly CultureInfo _enCulture = new("en-US");
    private readonly CultureInfo _viCulture = new("vi-VN");

    [Fact]
    public void FR_I18N_4_Time_format_24h_formats_consistently_across_cultures()
    {
        var time = new TimeOnly(14, 30);
        var morning = new TimeOnly(9, 5);

        // 24h format for en-US
        TimeFormattingHelper.FormatTime(time, TimeFormattingHelper.Format24Hours, _enCulture)
            .Should().Be("14:30");
        TimeFormattingHelper.FormatTime(morning, TimeFormattingHelper.Format24Hours, _enCulture)
            .Should().Be("09:05");

        // 24h format for vi-VN
        TimeFormattingHelper.FormatTime(time, TimeFormattingHelper.Format24Hours, _viCulture)
            .Should().Be("14:30");
        TimeFormattingHelper.FormatTime(morning, TimeFormattingHelper.Format24Hours, _viCulture)
            .Should().Be("09:05");
    }

    [Fact]
    public void FR_I18N_4_Time_format_12h_formats_with_culture_specific_designators()
    {
        var afternoon = new DateTimeOffset(2026, 11, 1, 14, 30, 0, TimeSpan.FromHours(7));
        var morning = new DateTimeOffset(2026, 11, 1, 9, 5, 0, TimeSpan.FromHours(7));

        // 12h format for en-US
        var enAfternoon = TimeFormattingHelper.FormatTime(afternoon, TimeFormattingHelper.Format12Hours, _enCulture);
        enAfternoon.Should().Be("2:30 PM");

        var enMorning = TimeFormattingHelper.FormatTime(morning, TimeFormattingHelper.Format12Hours, _enCulture);
        enMorning.Should().Be("9:05 AM");

        // 12h format for vi-VN (uses CH / SA designator in .NET)
        var viAfternoon = TimeFormattingHelper.FormatTime(afternoon, TimeFormattingHelper.Format12Hours, _viCulture);
        viAfternoon.Should().Contain("02:30");
        (viAfternoon.Contains("CH", StringComparison.OrdinalIgnoreCase) ||
         viAfternoon.Contains("PM", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void FR_I18N_4_Time_range_formatting_respects_format_and_culture()
    {
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(7));
        var end = new DateTimeOffset(2026, 11, 1, 10, 30, 0, TimeSpan.FromHours(7));

        var range24 = TimeFormattingHelper.FormatTimeRange(start, end, TimeFormattingHelper.Format24Hours, _enCulture);
        range24.Should().Be("09:00 – 10:30");

        var range12En = TimeFormattingHelper.FormatTimeRange(start, end, TimeFormattingHelper.Format12Hours, _enCulture);
        range12En.Should().Be("9:00 AM – 10:30 AM");
    }
}
