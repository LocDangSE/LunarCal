using System.Globalization;
using System.Windows;
using AwesomeAssertions;
using LunarCal.App.Converters;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class WeekAndDayViewModelTests
{
    private readonly IEventStore _eventStore;
    private readonly LocalizationService _localizationService;
    private readonly FakeSettingsStore _settingsStore;

    public WeekAndDayViewModelTests()
    {
        _eventStore = Substitute.For<IEventStore>();
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>()));

        _localizationService = new LocalizationService(SupportedCultures.Vietnamese);
        _settingsStore = new FakeSettingsStore();
        _settingsStore.SetString(SettingKeys.WeekStart, "Monday");
        _settingsStore.SetBool(SettingKeys.LunarEnabled, true);
    }

    [Fact]
    public void FR_UI_1_Timed_event_position_and_height_calculated_by_hour()
    {
        var date = new DateOnly(2026, 10, 19);
        var offset = TimeSpan.FromHours(7);

        // Event from 09:00 to 10:30 (1.5 hours)
        var ev1 = new CalendarEvent
        {
            Id = "ev-1",
            CalendarId = "cal-work",
            Title = "Morning Standup & Planning",
            StartUtc = new DateTimeOffset(2026, 10, 19, 9, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 10, 30, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        // Event from 14:15 to 15:00 (45 minutes = 0.75 hours)
        var ev2 = new CalendarEvent
        {
            Id = "ev-2",
            CalendarId = "cal-work",
            Title = "Design Review",
            StartUtc = new DateTimeOffset(2026, 10, 19, 14, 15, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 15, 0, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        // Very short event (10 minutes) -> clamped to MinEventHeight (24px)
        var ev3 = new CalendarEvent
        {
            Id = "ev-3",
            CalendarId = "cal-work",
            Title = "Quick Sync",
            StartUtc = new DateTimeOffset(2026, 10, 19, 16, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 16, 10, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        var col = new DayColumnViewModel(date, null, "Th 2", isToday: false, isLunarVisible: true);
        col.PopulateEvents(new[] { ev1, ev2, ev3 }, timezoneOffset: offset);

        col.TimedEvents.Should().HaveCount(3);

        // Event 1: 09:00 -> 9.0 * 48.0 = 432.0 px; Duration 1.5h -> 1.5 * 48.0 = 72.0 px
        var item1 = col.TimedEvents[0];
        item1.Title.Should().Be("Morning Standup & Planning");
        item1.Top.Should().Be(432.0);
        item1.Height.Should().Be(72.0);
        item1.TimeRangeText.Should().Be("09:00 – 10:30");

        // Event 2: 14:15 -> 14.25 * 48.0 = 684.0 px; Duration 0.75h -> 0.75 * 48.0 = 36.0 px
        var item2 = col.TimedEvents[1];
        item2.Title.Should().Be("Design Review");
        item2.Top.Should().Be(684.0);
        item2.Height.Should().Be(36.0);
        item2.TimeRangeText.Should().Be("14:15 – 15:00");

        // Event 3: 16:00 -> 16.0 * 48.0 = 768.0 px; Duration 10 min (8px) -> clamped to MinEventHeight (24.0 px)
        var item3 = col.TimedEvents[2];
        item3.Title.Should().Be("Quick Sync");
        item3.Top.Should().Be(768.0);
        item3.Height.Should().Be(DayColumnViewModel.MinEventHeight);
    }

    [Fact]
    public void FR_UI_1_Overlapping_timed_events_are_allocated_to_distinct_lanes()
    {
        var date = new DateOnly(2026, 10, 19);
        var offset = TimeSpan.FromHours(7);

        // Two overlapping events: 09:00-10:30 and 09:30-11:00
        var ev1 = new CalendarEvent
        {
            Id = "ev-1",
            CalendarId = "cal-1",
            Title = "Event A",
            StartUtc = new DateTimeOffset(2026, 10, 19, 9, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 10, 30, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        var ev2 = new CalendarEvent
        {
            Id = "ev-2",
            CalendarId = "cal-1",
            Title = "Event B",
            StartUtc = new DateTimeOffset(2026, 10, 19, 9, 30, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 11, 0, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        // Third non-overlapping event later in the afternoon: 14:00-15:00
        var ev3 = new CalendarEvent
        {
            Id = "ev-3",
            CalendarId = "cal-1",
            Title = "Event C",
            StartUtc = new DateTimeOffset(2026, 10, 19, 14, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 15, 0, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        var col = new DayColumnViewModel(date, null, "Th 2", isToday: false, isLunarVisible: true);
        col.PopulateEvents(new[] { ev1, ev2, ev3 }, timezoneOffset: offset);

        col.TimedEvents.Should().HaveCount(3);

        var itemA = col.TimedEvents.First(e => e.Title == "Event A");
        var itemB = col.TimedEvents.First(e => e.Title == "Event B");
        var itemC = col.TimedEvents.First(e => e.Title == "Event C");

        // Overlapping cluster A & B
        itemA.TotalColumns.Should().Be(2);
        itemB.TotalColumns.Should().Be(2);
        itemA.ColumnIndex.Should().Be(0);
        itemB.ColumnIndex.Should().Be(1);

        // Non-overlapping event C
        itemC.TotalColumns.Should().Be(1);
        itemC.ColumnIndex.Should().Be(0);
    }

    [Fact]
    public void FR_UI_1_All_day_events_are_separated_from_timed_events()
    {
        var date = new DateOnly(2026, 10, 19);
        var offset = TimeSpan.FromHours(7);

        var allDayEv = new CalendarEvent
        {
            Id = "ev-all-day",
            CalendarId = "cal-holiday",
            Title = "Company Retreat",
            StartUtc = new DateTimeOffset(2026, 10, 19, 0, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 20, 0, 0, 0, offset).ToUniversalTime(),
            AllDay = true,
        };

        var timedEv = new CalendarEvent
        {
            Id = "ev-timed",
            CalendarId = "cal-work",
            Title = "Workshop",
            StartUtc = new DateTimeOffset(2026, 10, 19, 10, 0, 0, offset).ToUniversalTime(),
            EndUtc = new DateTimeOffset(2026, 10, 19, 12, 0, 0, offset).ToUniversalTime(),
            AllDay = false,
        };

        var col = new DayColumnViewModel(date, null, "Th 2", isToday: false, isLunarVisible: true);
        col.PopulateEvents(new[] { allDayEv, timedEv }, timezoneOffset: offset);

        col.AllDayEvents.Should().HaveCount(1);
        col.AllDayEvents[0].Title.Should().Be("Company Retreat");

        col.TimedEvents.Should().HaveCount(1);
        col.TimedEvents[0].Title.Should().Be("Workshop");
    }

    [Fact]
    public void FR_UI_1_Column_headers_contain_lunar_date_and_format_mung_1()
    {
        // 1. Regular lunar day: 2026-10-16 is Lunar 2026-09-07 (mùng 7)
        var solarDate1 = new DateOnly(2026, 10, 16);
        var lunarDate1 = LunarCalendar.ToLunar(solarDate1);
        var col1 = new DayColumnViewModel(solarDate1, lunarDate1, "Th 6", isToday: false, isLunarVisible: true);
        col1.LunarDayText.Should().Be("7");

        // 2. Mùng 1 âm: 2023-01-22 is Lunar 2023-01-01 (Mùng 1 Tết Quý Mão) -> "1/1"
        var solarDate2 = new DateOnly(2023, 1, 22);
        var lunarDate2 = LunarCalendar.ToLunar(solarDate2);
        var col2 = new DayColumnViewModel(solarDate2, lunarDate2, "CN", isToday: false, isLunarVisible: true);
        col2.LunarDayText.Should().Be("1/1");

        // 3. Mùng 1 tháng nhuận: 2023-03-22 is Lunar 2023-02L-01 (Mùng 1 tháng 2 nhuận) -> "1/2n"
        var solarDate3 = new DateOnly(2023, 3, 22);
        var lunarDate3 = LunarCalendar.ToLunar(solarDate3);
        var col3 = new DayColumnViewModel(solarDate3, lunarDate3, "Th 4", isToday: false, isLunarVisible: true);
        col3.LunarDayText.Should().Be("1/2n");

        // 4. FR-LUNAR-6: Lunar hidden when master toggle is off
        col3.SetLunarVisibility(false);
        col3.LunarDayText.Should().BeEmpty();
    }

    [Fact]
    public void FR_UI_1_Now_indicator_position_is_calculated_for_today()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var notToday = today.AddDays(1);

        // Fixed mock time: 10:30:00 (10.5 hours) -> 10.5 * 48.0 = 504.0 px
        var fixedNow = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 30, 0, TimeSpan.FromHours(7));

        var todayCol = new DayColumnViewModel(today, null, "Hôm nay", isToday: true, isLunarVisible: true);
        todayCol.UpdateNowIndicator(fixedNow);
        todayCol.ShowNowIndicator.Should().BeTrue();
        todayCol.NowIndicatorTop.Should().Be(504.0);

        var notTodayCol = new DayColumnViewModel(notToday, null, "Ngày mai", isToday: false, isLunarVisible: true);
        notTodayCol.UpdateNowIndicator(fixedNow);
        notTodayCol.ShowNowIndicator.Should().BeFalse();
        notTodayCol.NowIndicatorTop.Should().Be(-1.0);
    }

    [Fact]
    public async Task FR_UI_1_WeekViewModel_builds_7_columns_and_supports_navigation()
    {
        using var vm = new WeekViewModel(_eventStore, _localizationService, _settingsStore);
        var anchorDate = new DateOnly(2026, 10, 21); // Wednesday
        await vm.SetDisplayDateAsync(anchorDate);

        // Starts on Monday
        vm.WeekStart.Should().Be(DayOfWeek.Monday);
        vm.DayColumns.Should().HaveCount(7);
        vm.WeekStartDate.Should().Be(new DateOnly(2026, 10, 19)); // Monday
        vm.WeekEndDate.Should().Be(new DateOnly(2026, 10, 25));   // Sunday

        // Verify column dates
        vm.DayColumns[0].Date.Should().Be(new DateOnly(2026, 10, 19));
        vm.DayColumns[6].Date.Should().Be(new DateOnly(2026, 10, 25));

        // Column headers have lunar dates
        vm.DayColumns[0].LunarDayText.Should().NotBeEmpty();

        // Title in Vietnamese
        vm.WeekTitle.Should().Contain("Tháng 10, 2026");

        // Change week start to Sunday
        vm.SetWeekStart(DayOfWeek.Sunday);
        await vm.SetDisplayDateAsync(anchorDate);
        vm.WeekStartDate.Should().Be(new DateOnly(2026, 10, 18)); // Sunday
        vm.DayColumns[0].Date.Should().Be(new DateOnly(2026, 10, 18));
    }

    [Fact]
    public async Task FR_UI_1_DayViewModel_builds_single_day_column_and_title()
    {
        using var vm = new DayViewModel(_eventStore, _localizationService, _settingsStore);
        var targetDate = new DateOnly(2026, 10, 19); // Monday
        await vm.SetDisplayDateAsync(targetDate);

        vm.DayColumn.Should().NotBeNull();
        vm.DayColumn!.Date.Should().Be(targetDate);
        vm.DayColumn!.SolarDayString.Should().Be("19");
        vm.DayColumn!.LunarDayText.Should().NotBeEmpty();

        vm.DayTitle.Should().Contain("19");
        vm.DayTitle.Should().Contain("Tháng 10, 2026");

        // Toggle lunar visibility
        vm.SetLunarVisibility(false);
        vm.DayColumn!.LunarDayText.Should().BeEmpty();
    }

    [Fact]
    public void FR_UI_1_Converters_calculate_lane_margin_and_width_correctly()
    {
        var posConv = new LaneMarginConverter();
        var widthConv = new LaneWidthConverter();

        // Total column width = 144px. 2 overlapping lanes (columnIndex 0 and 1).
        // availableWidth = 144 - 4 = 140px. laneWidth = 70px.
        // Event 0: left = 2.0 + 0 * 70 = 2.0. Margin = (2, 0, 0, 0).
        // Event 1: left = 2.0 + 1 * 70 = 72.0. Margin = (72, 0, 0, 0).
        // Width = 70 - 4 = 66px.

        var margin0 = (Thickness)posConv.Convert(new object[] { 144.0, 0, 2 }, typeof(Thickness), null!, CultureInfo.InvariantCulture);
        var margin1 = (Thickness)posConv.Convert(new object[] { 144.0, 1, 2 }, typeof(Thickness), null!, CultureInfo.InvariantCulture);
        var width = (double)widthConv.Convert(new object[] { 144.0, 2 }, typeof(double), null!, CultureInfo.InvariantCulture);

        margin0.Left.Should().Be(2.0);
        margin1.Left.Should().Be(72.0);
        width.Should().Be(66.0);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _dict = new(StringComparer.Ordinal);

        public void SetString(string key, string? value)
        {
            if (value is null)
            {
                _dict.Remove(key);
            }
            else
            {
                _dict[key] = value;
            }
        }

        public void SetBool(string key, bool value) => _dict[key] = value ? "true" : "false";

        public ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dict.TryGetValue(key, out var val) ? val : null);

        public ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            SetString(key, value);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(_dict);

        public ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_dict.TryGetValue(key, out var val) && bool.TryParse(val, out var b) ? b : defaultValue);

        public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
        {
            SetBool(key, value);
            return ValueTask.CompletedTask;
        }
    }
}
