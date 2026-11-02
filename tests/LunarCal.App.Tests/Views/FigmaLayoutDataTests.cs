using AwesomeAssertions;
using LunarCal.App.Converters;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// Text and colours the views show, checked against the sample content of the Figma frames (P2-R11..R15):
/// 01 month (1:2), S06 week (23:2), S07 day (23:493), S08 agenda (23:984) and the S09 panel (25:2).
/// The Figma sample day is 16/10/2026 = mùng 7 tháng 9 Bính Ngọ, ngày Quý Hợi, tiết Hàn lộ.
/// </summary>
public sealed class FigmaLayoutDataTests
{
    private static readonly TimeSpan _utc7 = TimeSpan.FromHours(7);
    private static readonly DateOnly _figmaDay = new(2026, 10, 16);

    private readonly LocalizationService _vi = new(SupportedCultures.Vietnamese);
    private readonly LocalizationService _en = new(SupportedCultures.English);

    [Fact]
    public void FR_UI_9_Top_bar_lunar_subtitles_match_figma()
    {
        // Figma 01 / S08: month; S06: week 12–18/10; S07: day
        CalendarText.LunarMonthHeader(_vi, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))
            .Should().Be("Âm lịch: tháng 8 – 9 năm Bính Ngọ");
        CalendarText.LunarWeekHeader(_vi, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18))
            .Should().Be("Âm lịch: 3 – 9 tháng 9 năm Bính Ngọ");
        CalendarText.LunarDayHeader(_vi, _figmaDay).Should().Be("Âm lịch: 7 tháng 9 năm Bính Ngọ · Quý Hợi");
        CalendarText.DayTitle(_vi, _figmaDay).Should().Be("Thứ 6, 16 Tháng 10, 2026");

        // English and the cases Figma does not draw: one lunar month, a week across two lunar months, Tết
        CalendarText.LunarMonthHeader(_en, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))
            .Should().Be("Lunar: months 8 – 9, year of Bính Ngọ");
        CalendarText.LunarWeekHeader(_vi, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13))
            .Should().Be("Âm lịch: 27/8 – 4/9 năm Bính Ngọ");
        CalendarText.LunarMonthHeader(_vi, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))
            .Should().Be("Âm lịch: tháng 12 năm Ất Tỵ – tháng 1 năm Bính Ngọ");
        CalendarText.DayTitle(_en, _figmaDay).Should().Be("Fri, October 16, 2026");
    }

    [Fact]
    public void FR_UI_10_Calendar_colours_use_the_figma_chip_pairs()
    {
        CalendarColors.Palette("#2F6BFF").Should().Be(new CalendarPalette("#2F6BFF", "#EAF1FF", "#1D4ED8"));
        CalendarColors.Palette("#12B76A").Should().Be(new CalendarPalette("#12B76A", "#E7F8EF", "#067647"));
        CalendarColors.Palette("#F79009").Should().Be(new CalendarPalette("#F79009", "#FFF4E5", "#B54708"));
        CalendarColors.Palette("#7A5AF8").Should().Be(new CalendarPalette("#7A5AF8", "#F1ECFF", "#5925DC"));

        // Any other Google colour: 12 % tint on white, 60 % of the colour for text
        CalendarColors.Palette("#D50000").Should().Be(new CalendarPalette("#D50000", "#FAE0E0", "#800000"));
        CalendarColors.Palette("not a colour").Should().Be(CalendarColors.Palette(null));
    }

    [Fact]
    public async Task FR_UI_1_Month_chips_show_start_time_and_calendar_colours()
    {
        var store = Store(
            [Calendar("work", "Công việc · Google", "#12B76A"), Calendar("ai", "Nhắc việc từ AI", "#7A5AF8")],
            [Timed("e1", "work", "Gym", 18, 0, 90), Timed("e2", "ai", "Họp nhóm", 9, 0, 60, ai: true)]);
        using var vm = new MonthViewModel(store, _vi, new TestDisplayPreferences());

        await vm.PopulateMonthAsync(new DateOnly(2026, 10, 1));

        var chips = vm.Days.Single(d => d.SolarDate == _figmaDay).Chips.Where(c => c.IsVisible).ToList();
        chips.Select(c => c.Title).Should().Equal("09:00 Họp nhóm", "18:00 Gym");
        chips[0].IsAi.Should().BeTrue();
        chips[0].Background.Should().Be("#F1ECFF");
        chips[0].Foreground.Should().Be("#5925DC");
        chips[1].IsAi.Should().BeFalse();
        chips[1].Background.Should().Be("#E7F8EF");
        chips[1].Foreground.Should().Be("#067647");
    }

    [Fact]
    public async Task FR_UI_1_Week_view_opens_at_7am_with_gmt_label_and_figma_headers()
    {
        var store = Store([Calendar("me", "Cá nhân · Google", "#2F6BFF")], [Timed("e1", "me", "Nộp bài Python", 14, 0, 90)]);
        using var vm = new WeekViewModel(store, _vi, new TestDisplayPreferences());

        await vm.SetDisplayDateAsync(_figmaDay);

        vm.InitialScrollOffset.Should().Be(7 * 48);
        vm.TimeZoneLabel.Should().Be("GMT+7");
        var friday = vm.DayColumns.Single(c => c.Date == _figmaDay);
        friday.ShortDayName.Should().Be("T6");
        friday.LunarDayText.Should().Be("7");
        friday.LunarHeaderText.Should().Be("Mùng 7 tháng 9 · Quý Hợi");
        var block = friday.TimedEvents.Single();
        block.Background.Should().Be("#EAF1FF");
        block.Foreground.Should().Be("#1D4ED8");
        block.DetailText.Should().Be("14:00 – 15:30 · Cá nhân · Google");
    }

    [Fact]
    public async Task FR_UI_3_Day_detail_panel_matches_figma_s09()
    {
        var store = Store(
            [Calendar("me", "Cá nhân · Google", "#2F6BFF"), Calendar("work", "Công việc · Google", "#12B76A")],
            [Timed("e1", "me", "Nộp bài Python", 14, 0, 90), Timed("e2", "work", "Gym", 18, 0, 90)]);
        using var vm = new DayDetailViewModel(store, _vi, new TestDisplayPreferences());

        await vm.SetDateAsync(_figmaDay, TestContext.Current.CancellationToken);

        vm.WeekdayName.Should().Be("Thứ Sáu");
        vm.SolarDateShort.Should().Be("16 Tháng 10, 2026");
        vm.LunarDayNumberText.Should().Be("7");
        vm.LunarFullText.Should().Be("Mùng 7 tháng 9 năm Bính Ngọ");
        vm.LunarMonthInfoText.Should().Be("Tháng 9 đủ (30 ngày) · không nhuận");
        vm.DayCanChiText.Should().Be("Quý Hợi");
        vm.MonthCanChiText.Should().Be("Mậu Tuất");
        vm.YearCanChiText.Should().Be("Bính Ngọ");
        vm.SolarTermRowText.Should().Be("Hàn lộ · từ 08/10");
        vm.EventsHeaderText.Should().Be("SỰ KIỆN (2)");
        vm.Events.Select(e => e.MetaText).Should().Equal(
            "14:00 – 15:30 · Cá nhân · Google",
            "18:00 – 19:30 · Công việc · Google");
    }

    [Fact]
    public async Task FR_UI_1_Agenda_lists_31_days_with_figma_labels_and_summary()
    {
        var store = Store(
            [Calendar("me", "Cá nhân · Google", "#2F6BFF"), Calendar("lunar", "Âm lịch · cục bộ", "#F79009")],
            [
                Timed("e1", "me", "Nộp bài Python", 14, 0, 90),
                AllDay("e2", "lunar", "Rằm tháng 9", new DateOnly(2026, 10, 24)),
                AllDay("e3", "lunar", "Mùng 1 tháng 10", new DateOnly(2026, 11, 9)),
                AllDay("e4", "lunar", "Ngoài khoảng", new DateOnly(2026, 11, 16)),
            ]);
        using var vm = new AgendaViewModel(store, _vi, new TestDisplayPreferences());

        await vm.SetDisplayDateAsync(_figmaDay);

        AgendaViewModel.Range(_figmaDay).Should().Be((_figmaDay, new DateOnly(2026, 11, 15)));
        vm.SummaryText.Should().Be("16/10 – 15/11/2026 · 3 sự kiện");
        vm.DayGroups.Select(g => g.DateLabel).Should().Equal("T6, Th10", "T7, Th10", "T2, Th11");
        vm.DayGroups.Select(g => g.LunarLabelText).Should().Equal("Âm: 7/9", "Âm: 15/9 · Rằm", "Âm: 1/10");
        vm.DayGroups[2].IsFirstLunarDay.Should().BeTrue();
    }

    [Fact]
    public void FR_UI_1_Day_view_blocks_use_the_figma_insets()
    {
        // S07 (25:91): an 800 px column (744 after the gutter) holds a 720 px block 8 px from the column line.
        var width = (double)new LaneWidthConverter().Convert([744.0, 1], typeof(double), "8,16", System.Globalization.CultureInfo.InvariantCulture);
        var margin = (System.Windows.Thickness)new LaneMarginConverter().Convert([744.0, 0, 1], typeof(System.Windows.Thickness), "8,16", System.Globalization.CultureInfo.InvariantCulture);

        width.Should().Be(720);
        margin.Left.Should().Be(8);
    }

    private static IEventStore Store(IReadOnlyList<CalendarInfo> calendars, IReadOnlyList<CalendarEvent> events)
    {
        var store = Substitute.For<IEventStore>();
        store.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(calendars));
        store.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var from = call.ArgAt<DateTimeOffset>(0);
                var to = call.ArgAt<DateTimeOffset>(1);
                return Task.FromResult<IReadOnlyList<CalendarEvent>>(events.Where(e => e.StartUtc < to && e.EndUtc > from).ToList());
            });
        return store;
    }

    private static CalendarInfo Calendar(string id, string name, string color) =>
        new() { Id = id, AccountId = "acc", Summary = name, Color = color };

    private static CalendarEvent Timed(string id, string calendarId, string title, int hour, int minute, int minutes, bool ai = false)
    {
        var start = new DateTimeOffset(_figmaDay.Year, _figmaDay.Month, _figmaDay.Day, hour, minute, 0, _utc7);
        return new CalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            Title = title,
            StartUtc = start.ToUniversalTime(),
            EndUtc = start.AddMinutes(minutes).ToUniversalTime(),
            ExtendedProperties = ai
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["createdBy"] = CalendarColors.AiCreatorTag }
                : new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private static CalendarEvent AllDay(string id, string calendarId, string title, DateOnly date)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, _utc7);
        return new CalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            Title = title,
            AllDay = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            StartUtc = start.ToUniversalTime(),
            EndUtc = start.AddDays(1).ToUniversalTime(),
        };
    }
}
