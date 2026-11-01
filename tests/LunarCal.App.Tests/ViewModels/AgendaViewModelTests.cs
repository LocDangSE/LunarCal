using System.Globalization;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class AgendaViewModelTests
{
    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();
    private readonly ISettingsStore _settingsStore = new InMemorySettingsStore();
    private readonly LocalizationService _localizationService = new(SupportedCultures.Vietnamese);

    [Fact]
    public async Task FR_UI_1_Agenda_groups_events_by_date()
    {
        // Arrange
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-work", AccountId = "acc1", Summary = "Công việc", Color = "#4285F4" },
        };

        var offset = TimeSpan.FromHours(7);
        var day1 = new DateTimeOffset(2026, 11, 5, 9, 0, 0, offset);
        var day2 = new DateTimeOffset(2026, 11, 6, 14, 0, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-1",
                CalendarId = "cal-work",
                Title = "Họp dự án",
                StartUtc = day1.ToUniversalTime(),
                EndUtc = day1.AddHours(1).ToUniversalTime(),
                AllDay = false,
            },
            new()
            {
                Id = "ev-2",
                CalendarId = "cal-work",
                Title = "Báo cáo tiến độ",
                StartUtc = day2.ToUniversalTime(),
                EndUtc = day2.AddHours(2).ToUniversalTime(),
                AllDay = false,
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);

        // Act
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Assert
        vm.HasEvents.Should().BeTrue();
        vm.DayGroups.Should().HaveCount(2);

        vm.DayGroups[0].Date.Should().Be(new DateOnly(2026, 11, 5));
        vm.DayGroups[0].Events.Should().HaveCount(1);
        vm.DayGroups[0].Events[0].Title.Should().Be("Họp dự án");

        vm.DayGroups[1].Date.Should().Be(new DateOnly(2026, 11, 6));
        vm.DayGroups[1].Events.Should().HaveCount(1);
        vm.DayGroups[1].Events[0].Title.Should().Be("Báo cáo tiến độ");
    }

    [Fact]
    public async Task FR_UI_1_Agenda_filters_events_by_calendar_chip()
    {
        // Arrange
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-work", AccountId = "acc1", Summary = "Công việc", Color = "#4285F4" },
            new() { Id = "cal-personal", AccountId = "acc1", Summary = "Cá nhân", Color = "#0F9D58" },
        };

        var offset = TimeSpan.FromHours(7);
        var day = new DateTimeOffset(2026, 11, 5, 10, 0, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-work",
                CalendarId = "cal-work",
                Title = "Họp công ty",
                StartUtc = day.ToUniversalTime(),
                EndUtc = day.AddHours(1).ToUniversalTime(),
            },
            new()
            {
                Id = "ev-personal",
                CalendarId = "cal-personal",
                Title = "Đi siêu thị",
                StartUtc = day.AddHours(2).ToUniversalTime(),
                EndUtc = day.AddHours(3).ToUniversalTime(),
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Initial state: both calendars active
        vm.CalendarFilters.Should().HaveCount(2);
        vm.IsAllCalendarsSelected.Should().BeTrue();
        vm.DayGroups.Should().HaveCount(1);
        vm.DayGroups[0].Events.Should().HaveCount(2);

        // Act: Deselect "Công việc" chip
        var workChip = vm.CalendarFilters.First(c => c.Id == "cal-work");
        workChip.ToggleCommand.Execute(null);

        // Assert: only "Cá nhân" event remains
        workChip.IsSelected.Should().BeFalse();
        vm.IsAllCalendarsSelected.Should().BeFalse();
        vm.DayGroups[0].Events.Should().HaveCount(1);
        vm.DayGroups[0].Events[0].Title.Should().Be("Đi siêu thị");

        // Act: Toggle All Calendars (should toggle all to true since not all were selected)
        vm.ToggleAllCalendarsCommand.Execute(null);
        vm.IsAllCalendarsSelected.Should().BeTrue();
        vm.CalendarFilters.All(c => c.IsSelected).Should().BeTrue();
        vm.DayGroups[0].Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task FR_UI_1_Agenda_displays_lunar_date_in_group_header()
    {
        // Arrange: 2026-11-09 is 2026-10-01 Lunar (Mùng 1 tháng 10 Âm lịch)
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-1", AccountId = "acc1", Summary = "Lịch chính" },
        };

        var offset = TimeSpan.FromHours(7);
        var day = new DateTimeOffset(2026, 11, 9, 8, 0, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-mung1",
                CalendarId = "cal-1",
                Title = "Lễ mùng một",
                StartUtc = day.ToUniversalTime(),
                EndUtc = day.AddHours(1).ToUniversalTime(),
                AllDay = true,
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);

        // Act
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Assert
        vm.DayGroups.Should().HaveCount(1);
        var group = vm.DayGroups[0];
        group.IsFirstLunarDay.Should().BeTrue();
        group.LunarDayText.Should().Be("1/10 ÂL");
        group.IsLunarVisible.Should().BeTrue();
    }

    [Fact]
    public async Task FR_LUNAR_6_Agenda_hides_lunar_date_when_disabled()
    {
        // Arrange
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-1", AccountId = "acc1", Summary = "Lịch chính" },
        };

        var offset = TimeSpan.FromHours(7);
        var day = new DateTimeOffset(2026, 11, 5, 8, 0, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-1",
                CalendarId = "cal-1",
                Title = "Sự kiện",
                StartUtc = day.ToUniversalTime(),
                EndUtc = day.AddHours(1).ToUniversalTime(),
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Initial: lunar is visible
        vm.DayGroups[0].LunarDayText.Should().NotBeEmpty();

        // Act
        vm.SetLunarVisibility(false);

        // Assert
        vm.IsLunarVisible.Should().BeFalse();
        vm.DayGroups[0].IsLunarVisible.Should().BeFalse();
        vm.DayGroups[0].LunarDayText.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_UI_1_Agenda_sorts_all_day_events_before_timed_events()
    {
        // Arrange
        var calendars = new List<CalendarInfo>
        {
            new() { Id = "cal-1", AccountId = "acc1", Summary = "Chính", Color = "#4285F4" },
        };

        var offset = TimeSpan.FromHours(7);
        var dayStart = new DateTimeOffset(2026, 11, 15, 0, 0, 0, offset);
        var timedStart = new DateTimeOffset(2026, 11, 15, 9, 30, 0, offset);

        var events = new List<CalendarEvent>
        {
            new()
            {
                Id = "ev-timed",
                CalendarId = "cal-1",
                Title = "Họp đối tác",
                StartUtc = timedStart.ToUniversalTime(),
                EndUtc = timedStart.AddHours(1).ToUniversalTime(),
                AllDay = false,
            },
            new()
            {
                Id = "ev-allday",
                CalendarId = "cal-1",
                Title = "Sinh nhật đồng nghiệp",
                StartUtc = dayStart.ToUniversalTime(),
                EndUtc = dayStart.AddDays(1).ToUniversalTime(),
                AllDay = true,
            },
        };

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(calendars);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(events);

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);

        // Act
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Assert: All-day event must be listed first
        var group = vm.DayGroups[0];
        group.Events.Should().HaveCount(2);
        group.Events[0].Title.Should().Be("Sinh nhật đồng nghiệp");
        group.Events[0].AllDay.Should().BeTrue();
        group.Events[0].TimeDisplay.Should().Be("Cả ngày");

        group.Events[1].Title.Should().Be("Họp đối tác");
        group.Events[1].AllDay.Should().BeFalse();
        group.Events[1].TimeDisplay.Should().Be("09:30 – 10:30");
    }

    [Fact]
    public async Task FR_UI_1_Agenda_empty_state_when_no_events()
    {
        // Arrange
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new List<CalendarInfo>());
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CalendarEvent>());

        var vm = new AgendaViewModel(_eventStore, _localizationService, _settingsStore);

        // Act
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        // Assert
        vm.HasEvents.Should().BeFalse();
        vm.DayGroups.Should().BeEmpty();
    }
}
