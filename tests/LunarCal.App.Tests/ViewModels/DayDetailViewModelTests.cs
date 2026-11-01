using System.Globalization;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class DayDetailViewModelTests
{
    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();
    private readonly ISettingsStore _settingsStore = new InMemorySettingsStore();
    private readonly TestDisplayPreferences _preferences = new();
    private readonly LocalizationService _localizationService = new(SupportedCultures.Vietnamese);

    [Fact]
    public async Task FR_UI_3_Day_detail_shows_can_chi_and_tiet_khi()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: reference date 2026-10-16 from Figma S09
        // 16/10/2026 = mùng 7 tháng 9 năm Bính Ngọ, ngày Quý Hợi, tháng Mậu Tuất, tiết Hàn lộ (195°)
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarInfo>());
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarEvent>());

        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);

        // Act
        await vm.SetDateAsync(new DateOnly(2026, 10, 16), ct);

        // Assert
        vm.Date.Should().Be(new DateOnly(2026, 10, 16));
        vm.HasLunarDate.Should().BeTrue();
        vm.LunarDay.Should().Be(7);
        vm.LunarDayNumberText.Should().Be("7");
        vm.LunarMonth.Should().Be(9);
        vm.LunarMonthText.Should().Be("Tháng 9");

        // Can Chi (FR-UI-3)
        vm.DayCanChiText.Should().Be("Quý Hợi");
        vm.MonthCanChiText.Should().Be("Mậu Tuất");
        vm.YearCanChiText.Should().Be("Bính Ngọ");

        // Tiết khí (FR-UI-3, ADR-0007)
        vm.SolarTermText.Should().Be("Tiết Hàn lộ");
        vm.SolarTermDegreesText.Should().Be("195°");
    }

    [Fact]
    public async Task FR_I18N_5_Day_detail_shows_english_glosses_in_en_US()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarInfo>());
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarEvent>());

        _localizationService.SetCulture(SupportedCultures.English);
        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);

        // Act: 2026-10-16 in English UI mode
        await vm.SetDateAsync(new DateOnly(2026, 10, 16), ct);

        // Assert: Can Chi retains Vietnamese proper name + English gloss (FR-I18N-5)
        vm.DayCanChiText.Should().Contain("Quý Hợi");
        vm.DayCanChiText.Should().Contain("Yin Water Pig");

        vm.MonthCanChiText.Should().Contain("Mậu Tuất");
        vm.MonthCanChiText.Should().Contain("Yang Earth Dog");

        vm.YearCanChiText.Should().Contain("Bính Ngọ");
        vm.YearCanChiText.Should().Contain("Year of the Fire Horse");

        // Tiết khí displays Vietnamese name + English name (FR-I18N-5)
        vm.SolarTermText.Should().Contain("Hàn lộ");
        vm.SolarTermText.Should().Contain("Cold Dew");
    }

    [Fact]
    public async Task FR_UI_3_Day_detail_shows_first_lunar_day_and_leap_month()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarInfo>());
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarEvent>());

        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);

        // Act 1: 2026-11-09 is 2026-10-01 Lunar (Mùng 1 tháng 10)
        await vm.SetDateAsync(new DateOnly(2026, 11, 9), ct);
        vm.IsFirstLunarDay.Should().BeTrue();
        vm.LunarDayNumberText.Should().Be("1");

        // Act 2: 2025-07-25 is in leap month 6 (Tháng 6 nhuận năm Ất Tỵ)
        await vm.SetDateAsync(new DateOnly(2025, 7, 25), ct);
        vm.IsLeapMonth.Should().BeTrue();
        vm.LunarMonthText.Should().Contain("Nhuận");

        // Switch to English and verify "Leap" suffix
        _localizationService.SetCulture(SupportedCultures.English);
        await vm.SetDateAsync(new DateOnly(2025, 7, 25), ct);
        vm.LunarMonthText.Should().Contain("Leap");
    }

    [Fact]
    public async Task FR_LUNAR_6_Day_detail_hides_lunar_info_when_disabled()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarInfo>());
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarEvent>());

        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDateAsync(new DateOnly(2026, 10, 16), ct);
        vm.IsLunarVisible.Should().BeTrue();

        // Act: disable lunar display
        vm.SetLunarVisibility(false);

        // Assert
        vm.IsLunarVisible.Should().BeFalse();
    }

    [Fact]
    public async Task FR_UI_3_Day_detail_loads_and_sorts_day_events()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-work", AccountId = "acc1", Summary = "Công việc", Color = "#4285F4" },
        };

        var offset = TimeSpan.FromHours(7);
        var targetDate = new DateOnly(2026, 10, 16);
        var allDayStart = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue), offset);
        var timedStart = new DateTimeOffset(2026, 10, 16, 14, 0, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-timed",
                CalendarId = "cal-work",
                Title = "Họp kỹ thuật",
                StartUtc = timedStart.ToUniversalTime(),
                EndUtc = timedStart.AddHours(1).ToUniversalTime(),
                AllDay = false,
                Location = "Phòng họp A",
            },
            new()
            {
                Id = "ev-allday",
                CalendarId = "cal-work",
                Title = "Nghỉ phép",
                StartUtc = allDayStart.ToUniversalTime(),
                EndUtc = allDayStart.AddDays(1).ToUniversalTime(),
                AllDay = true,
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);

        // Act
        await vm.SetDateAsync(targetDate, ct);

        // Assert: all-day must be listed before timed event
        vm.HasEvents.Should().BeTrue();
        vm.Events.Should().HaveCount(2);

        vm.Events[0].Title.Should().Be("Nghỉ phép");
        vm.Events[0].AllDay.Should().BeTrue();
        vm.Events[0].TimeDisplay.Should().Be("Cả ngày");

        vm.Events[1].Title.Should().Be("Họp kỹ thuật");
        vm.Events[1].AllDay.Should().BeFalse();
        vm.Events[1].TimeDisplay.Should().Be("14:00 – 15:00");
        vm.Events[1].HasLocation.Should().BeTrue();
        vm.Events[1].Location.Should().Be("Phòng họp A");
    }

    [Fact]
    public async Task FR_UI_3_Day_detail_commands_raise_expected_events()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);
        var testDate = new DateOnly(2026, 10, 16);
        await vm.SetDateAsync(testDate, ct);

        DateOnly? addedEventDate = null;
        DateOnly? addedRuleDate = null;
        bool closeCalled = false;

        vm.AddEventRequested += (_, d) => addedEventDate = d;
        vm.AddLunarRuleRequested += (_, d) => addedRuleDate = d;
        vm.CloseRequested += (_, _) => closeCalled = true;

        // Act
        vm.AddEventCommand.Execute(null);
        vm.AddLunarRuleCommand.Execute(null);
        vm.CloseCommand.Execute(null);

        // Assert
        addedEventDate.Should().Be(testDate);
        addedRuleDate.Should().Be(testDate);
        closeCalled.Should().BeTrue();
    }
}
