using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.Localization;

/// <summary>
/// All calendar wording comes from resx in both languages (FR-I18N-3, FR-I18N-4, FR-I18N-5, FR-UI-2).
/// </summary>
public sealed class CalendarTextTests
{
    private static readonly LunarDate _leapFirstDay = LunarCalendar.ToLunar(new DateOnly(2023, 3, 22)); // 1/2 leap

    private readonly LocalizationService _vi = new(SupportedCultures.Vietnamese);
    private readonly LocalizationService _en = new(SupportedCultures.English);

    [Fact]
    public void FR_I18N_4_Date_titles_follow_culture()
    {
        var sunday = new DateOnly(2026, 11, 1);

        CalendarText.MonthYear(_vi, sunday).Should().Be("Tháng 11, 2026");
        CalendarText.MonthYear(_en, sunday).Should().Be("November 2026");

        CalendarText.DayFull(_vi, sunday).Should().Be("Chủ Nhật, 1 Tháng 11, 2026");
        CalendarText.DayFull(_en, sunday).Should().Be("Sunday, November 1, 2026");

        CalendarText.DayShort(_vi, sunday).Should().Be("1 Tháng 11, 2026");
        CalendarText.DayShort(_en, sunday).Should().Be("Nov 1, 2026");

        CalendarText.WeekRange(_vi, new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 21)).Should().Be("15 – 21 Tháng 11, 2026");
        CalendarText.WeekRange(_en, new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 21)).Should().Be("Nov 15 – 21, 2026");
        CalendarText.WeekRange(_vi, new DateOnly(2026, 11, 29), new DateOnly(2026, 12, 5)).Should().Be("29/11 – 5/12, 2026");
        CalendarText.WeekRange(_en, new DateOnly(2026, 11, 29), new DateOnly(2026, 12, 5)).Should().Be("Nov 29 – Dec 5, 2026");

        CalendarText.AgendaDay(_vi, sunday, isToday: true).Should().Be("Hôm nay, 1 Tháng 11");
        CalendarText.AgendaDay(_en, sunday, isToday: false).Should().Be("Sunday, Nov 1");
    }

    [Fact]
    public void FR_I18N_4_Weekday_headers_follow_culture()
    {
        CalendarText.WeekdayShort(_vi, DayOfWeek.Monday).Should().Be("Th 2");
        CalendarText.WeekdayShort(_vi, DayOfWeek.Sunday).Should().Be("CN");
        CalendarText.WeekdayShort(_en, DayOfWeek.Monday).Should().Be("Mon");
        CalendarText.WeekdayMin(_vi, DayOfWeek.Saturday).Should().Be("T7");
        CalendarText.WeekdayMin(_en, DayOfWeek.Saturday).Should().Be("Sa");
    }

    [Fact]
    public void FR_UI_2_Leap_marker_and_month_names_follow_culture()
    {
        CalendarText.LunarCell(_leapFirstDay, CalendarText.LeapMarker(_vi)).Should().Be("1/2n");
        CalendarText.LunarCell(_leapFirstDay, CalendarText.LeapMarker(_en)).Should().Be("1/2L");
        CalendarText.LunarCell(null, "n").Should().BeEmpty();

        CalendarText.LunarMonthName(_vi, 6, isLeap: true).Should().Be("Tháng 6 (Nhuận)");
        CalendarText.LunarMonthName(_en, 6, isLeap: true).Should().Be("Month 6 (Leap)");
        CalendarText.LunarMonthName(_en, 9, isLeap: false).Should().Be("Month 9");
    }

    [Fact]
    public async Task FR_I18N_4_Month_view_header_and_leap_cell_in_english()
    {
        var store = Substitute.For<IEventStore>();
        store.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>()));
        var preferences = new TestDisplayPreferences { WeekStart = DayOfWeek.Sunday };

        using var vm = new MonthViewModel(store, _en, preferences);
        await vm.InitializeSettingsAsync();
        await vm.SetDisplayDateAsync(new DateOnly(2023, 3, 1));

        vm.MonthTitle.Should().Be("March 2023");
        vm.WeekdayHeaders.Should().Equal("Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat");
        vm.Days.Single(d => d.SolarDate == new DateOnly(2023, 3, 22)).LunarDayText.Should().Be("1/2L");
    }

    [Fact]
    public void FR_I18N_5_Every_new_calendar_key_has_vietnamese_and_english_values()
    {
        string[] keys =
        [
            "format.month_year", "format.day_full", "format.day_short", "format.week_same_month",
            "format.week_cross_month", "format.agenda_day", "format.agenda_today", "lunar.leap_marker",
            "lunar.month_name", "lunar.month_name_leap", "lunar.short_suffix", "lunar.day_label",
            "lunar.header_day", "lunar.header_month", "ai.command_placeholder", "minical.prev_month",
        ];

        foreach (var key in keys)
        {
            var vi = _vi.GetString(key, SupportedCultures.Vietnamese);
            var en = _en.GetString(key, SupportedCultures.English);
            vi.Should().NotStartWith("!", $"'{key}' must exist in Vietnamese");
            en.Should().NotStartWith("!", $"'{key}' must exist in English");
            en.Should().NotBe(vi, $"'{key}' must be translated, not fall back to Vietnamese");
        }
    }
}
