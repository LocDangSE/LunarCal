using System.Globalization;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class MonthViewModelTests
{
    private readonly IEventStore _eventStore;
    private readonly LocalizationService _localizationService;
    private readonly FakeSettingsStore _settingsStore;
    private readonly TestDisplayPreferences _preferences = new();

    public MonthViewModelTests()
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
    public async Task FR_UI_1_Month_grid_has_42_cells_and_7_weekday_headers()
    {
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.PopulateMonthAsync(new DateOnly(2026, 11, 1));

        vm.Days.Should().HaveCount(42);
        vm.WeekdayHeaders.Should().HaveCount(7);
        vm.WeekdayHeaders[0].Should().Be("Th 2");
        vm.WeekdayHeaders[6].Should().Be("CN");

        // Verify current month flagging
        var novFirst = vm.Days.First(d => d.SolarDate == new DateOnly(2026, 11, 1));
        novFirst.IsCurrentMonth.Should().BeTrue();

        var octDay = vm.Days.First(d => d.SolarDate.Month == 10);
        octDay.IsCurrentMonth.Should().BeFalse();
    }

    [Fact]
    public async Task FR_UI_2_First_lunar_day_shows_day_slash_month()
    {
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);

        // 1. Regular first lunar day (Tết Quý Mão 2023: 2023-01-22 is Lunar 2023-01-01)
        await vm.PopulateMonthAsync(new DateOnly(2023, 1, 1));
        var tetDay = vm.Days.First(d => d.SolarDate == new DateOnly(2023, 1, 22));

        tetDay.LunarDate.Should().NotBeNull();
        tetDay.LunarDate!.Value.Day.Should().Be(1);
        tetDay.LunarDate!.Value.Month.Should().Be(1);
        tetDay.IsFirstLunarDay.Should().BeTrue();
        tetDay.IsLeapMonth.Should().BeFalse();
        tetDay.LunarDayText.Should().Be("1/1");

        // Regular lunar day (e.g. 15 âm - Rằm tháng Giêng 2023-02-05)
        var ramDay = vm.Days.First(d => d.SolarDate == new DateOnly(2023, 2, 5));
        ramDay.LunarDay.Should().Be(15);
        ramDay.IsFirstLunarDay.Should().BeFalse();
        ramDay.LunarDayText.Should().Be("15");

        // 2. Leap month first lunar day: Year 2023 has leap month 2 (tháng 2 nhuận)
        // 2023-03-22 is Lunar 2023-02L-01 (Mùng 1 tháng 2 nhuận)
        await vm.PopulateMonthAsync(new DateOnly(2023, 3, 1));
        var leapDay = vm.Days.First(d => d.SolarDate == new DateOnly(2023, 3, 22));

        leapDay.LunarDate.Should().NotBeNull();
        leapDay.LunarDate!.Value.Day.Should().Be(1);
        leapDay.LunarDate!.Value.Month.Should().Be(2);
        leapDay.IsLeapMonth.Should().BeTrue();
        leapDay.IsFirstLunarDay.Should().BeTrue();
        leapDay.LunarDayText.Should().Be("1/2n");
    }

    [Fact]
    public async Task FR_LUNAR_6_Lunar_text_hidden_when_disabled()
    {
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.PopulateMonthAsync(new DateOnly(2026, 11, 1));

        // When visible by default
        vm.IsLunarVisible.Should().BeTrue();
        vm.Days.Any(d => !string.IsNullOrEmpty(d.LunarDayText)).Should().BeTrue();

        // When toggled off (FR-LUNAR-6)
        vm.SetLunarVisibility(false);
        vm.IsLunarVisible.Should().BeFalse();

        foreach (var day in vm.Days)
        {
            day.IsLunarVisible.Should().BeFalse();
            day.LunarDayText.Should().BeEmpty();
        }

        // When toggled back on
        vm.SetLunarVisibility(true);
        vm.IsLunarVisible.Should().BeTrue();
        var cell = vm.Days.First(d => d.SolarDate == new DateOnly(2026, 11, 1));
        cell.IsLunarVisible.Should().BeTrue();
        cell.LunarDayText.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NFR_PERF_Month_grid_generation_takes_less_than_100_ms()
    {
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);

        // Benchmark 12 different months
        for (int m = 1; m <= 12; m++)
        {
            var target = new DateOnly(2026, m, 1);
            await vm.PopulateMonthAsync(target);

            vm.RenderDuration.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
                $"Month grid render for 2026-{m} must be < 100ms per NFR-PERF, but took {vm.RenderDuration.TotalMilliseconds}ms");
        }
    }

    [Fact]
    public async Task Navigation_and_selection_work_correctly()
    {
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(new DateOnly(2026, 6, 1));
        vm.DisplayDate.Should().Be(new DateOnly(2026, 6, 1));

        // Next month
        await vm.NextMonthCommand.ExecuteAsync(null);
        vm.DisplayDate.Should().Be(new DateOnly(2026, 7, 1));

        // Previous month
        await vm.PreviousMonthCommand.ExecuteAsync(null);
        vm.DisplayDate.Should().Be(new DateOnly(2026, 6, 1));

        // Select day
        DateOnly? raisedDate = null;
        vm.DaySelected += (s, d) => raisedDate = d;

        var targetCell = vm.Days.First(d => d.SolarDate == new DateOnly(2026, 6, 15));
        vm.SelectDayCommand.Execute(targetCell);

        vm.SelectedDay.Should().Be(targetCell);
        targetCell.IsSelected.Should().BeTrue();
        raisedDate.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task Events_chips_and_more_counter_are_populated_correctly()
    {
        var targetDate = new DateOnly(2026, 11, 15);
        var baseTime = new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.FromHours(7));

        var events = new List<CalendarEvent>
        {
            new() { Id = "e1", CalendarId = "c1", Title = "Họp nhóm", StartUtc = baseTime, EndUtc = baseTime.AddHours(1) },
            new() { Id = "e2", CalendarId = "c1", Title = "Ăn trưa", StartUtc = baseTime.AddHours(1), EndUtc = baseTime.AddHours(2) },
            new() { Id = "e3", CalendarId = "c1", Title = "Review code", StartUtc = baseTime.AddHours(2), EndUtc = baseTime.AddHours(3) },
            new() { Id = "e4", CalendarId = "c1", Title = "Chạy bộ", StartUtc = baseTime.AddHours(3), EndUtc = baseTime.AddHours(4) },
            new() { Id = "e5", CalendarId = "c1", Title = "Học tiếng Anh", StartUtc = baseTime.AddHours(4), EndUtc = baseTime.AddHours(5) },
        };

        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(events));

        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.PopulateMonthAsync(targetDate);

        var targetCell = vm.Days.First(d => d.SolarDate == targetDate);
        targetCell.Events.Should().HaveCount(5);
        targetCell.VisibleEvents.Should().HaveCount(3);
        targetCell.VisibleEvents[0].Title.Should().Be("Họp nhóm");
        targetCell.HasMoreEvents.Should().BeTrue();
        targetCell.MoreEventsCount.Should().Be(2);
        targetCell.MoreEventsText.Should().Be("+2 sự kiện khác");
    }

    [Fact]
    public async Task FR_UI_7_Month_grid_starts_on_monday_or_sunday_according_to_setting()
    {
        // 2026-11-01 is a Sunday
        var targetDate = new DateOnly(2026, 11, 1);

        // 1. Grid starting on Monday (vi-VN default)
        _preferences.WeekStart = DayOfWeek.Monday;
        using var vmMonday = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vmMonday.InitializeSettingsAsync();
        await vmMonday.PopulateMonthAsync(targetDate);

        vmMonday.WeekStart.Should().Be(DayOfWeek.Monday);
        vmMonday.WeekdayHeaders[0].Should().Be("Th 2");
        vmMonday.WeekdayHeaders[6].Should().Be("CN");
        vmMonday.Days[0].SolarDate.DayOfWeek.Should().Be(DayOfWeek.Monday);

        // Since 2026-11-01 is Sunday, with Monday start, it appears at index 6 (last day of first week row)
        vmMonday.Days[6].SolarDate.Should().Be(new DateOnly(2026, 11, 1));

        // 2. Grid starting on Sunday (en-US default / user setting)
        _preferences.WeekStart = DayOfWeek.Sunday;
        using var vmSunday = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vmSunday.InitializeSettingsAsync();
        await vmSunday.PopulateMonthAsync(targetDate);

        vmSunday.WeekStart.Should().Be(DayOfWeek.Sunday);
        vmSunday.WeekdayHeaders[0].Should().Be("CN");
        vmSunday.WeekdayHeaders[1].Should().Be("Th 2");
        vmSunday.Days[0].SolarDate.DayOfWeek.Should().Be(DayOfWeek.Sunday);

        // Since 2026-11-01 is Sunday, with Sunday start, it appears right at index 0
        vmSunday.Days[0].SolarDate.Should().Be(new DateOnly(2026, 11, 1));
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
