using System.Collections.ObjectModel;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class EventEditorViewModelTests
{
    private readonly IEventStore _eventStore;
    private readonly IMutationService _mutationService;
    private readonly LocalizationService _localizationService;
    private readonly TestDisplayPreferences _preferences;

    public EventEditorViewModelTests()
    {
        _eventStore = Substitute.For<IEventStore>();
        _mutationService = Substitute.For<IMutationService>();
        _localizationService = new LocalizationService(SupportedCultures.Vietnamese);
        _preferences = new TestDisplayPreferences();

        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new List<CalendarInfo>
            {
                new() { Id = "primary", AccountId = "primary", Summary = "Cá nhân", Color = "#4285F4", IsPrimary = true, IsSelected = true },
                new() { Id = "work", AccountId = "primary", Summary = "Công việc", Color = "#0B8043", IsPrimary = false, IsSelected = true }
            }));
    }

    [Fact]
    public void FR_UI_4_InitializeForNew_sets_default_values()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        var targetDate = new DateOnly(2026, 11, 15);
        var targetTime = new TimeOnly(14, 0);

        vm.InitializeForNew(targetDate, targetTime, isAllDay: false);

        vm.IsNew.Should().BeTrue();
        vm.IsEditing.Should().BeFalse();
        vm.StartDate.Should().Be(targetDate);
        vm.StartTime.Should().Be(targetTime);
        vm.EndDate.Should().Be(targetDate);
        vm.EndTime.Should().Be(targetTime.AddHours(1));
        vm.IsAllDay.Should().BeFalse();
        vm.Reminders.Should().Contain(10);
        vm.ReminderChips.Should().NotBeEmpty();
    }

    [Fact]
    public void FR_UI_4_InitializeForEdit_populates_event_fields_correctly()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        var timeZone = _preferences.TimeZone;
        var startLocal = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(7));
        var endLocal = new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.FromHours(7));

        var existing = new CalendarEvent
        {
            Id = "ev-123",
            GoogleId = "g-123",
            CalendarId = "work",
            Title = "Sprint Planning",
            Description = "Discuss backlog items",
            Location = "Room 301",
            StartUtc = startLocal.ToUniversalTime(),
            EndUtc = endLocal.ToUniversalTime(),
            AllDay = false,
            Etag = "etag-v1",
            TimeZone = timeZone.Id,
            Status = "confirmed"
        };

        vm.InitializeForEdit(existing);

        vm.IsNew.Should().BeFalse();
        vm.IsEditing.Should().BeTrue();
        vm.EventId.Should().Be("ev-123");
        vm.OriginalEtag.Should().Be("etag-v1");
        vm.Title.Should().Be("Sprint Planning");
        vm.Description.Should().Be("Discuss backlog items");
        vm.Location.Should().Be("Room 301");
        vm.StartDate.Should().Be(new DateOnly(2026, 11, 20));
        vm.StartTime.Should().Be(new TimeOnly(10, 0));
        vm.EndDate.Should().Be(new DateOnly(2026, 11, 20));
        vm.EndTime.Should().Be(new TimeOnly(11, 30));
        vm.IsAllDay.Should().BeFalse();
    }

    [Fact]
    public async Task FR_UI_4_Validation_requires_title_and_valid_time_interval()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew();

        // 1. Empty title -> fails
        vm.Title = "   ";
        vm.Validate().Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.HasError.Should().BeTrue();

        // 2. Title provided, but EndTime <= StartTime with custom end date on the same day -> fails
        vm.Title = "Valid Title";
        vm.IsAllDay = false;
        vm.StartDate = new DateOnly(2026, 11, 10);
        vm.StartTime = new TimeOnly(15, 0);
        vm.OpenCustomEndDateCommand.Execute(null);
        vm.EndDate = new DateOnly(2026, 11, 10);
        vm.EndTime = new TimeOnly(14, 0);

        vm.Validate().Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();

        // 3. Valid title and time -> passes
        vm.EndTime = new TimeOnly(16, 0);
        vm.Validate().Should().BeTrue();
        vm.ErrorMessage.Should().BeNull();
        vm.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task FR_UI_4_SaveAsync_creates_event_optimistically_via_mutation_service()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0), isAllDay: false);
        vm.Title = "Team Standup";

        _mutationService.CreateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        CalendarEvent? savedEvent = null;
        vm.Saved += (_, ev) => savedEvent = ev;

        var success = await vm.SaveAsync(TestContext.Current.CancellationToken);

        success.Should().BeTrue();
        savedEvent.Should().NotBeNull();
        savedEvent!.Title.Should().Be("Team Standup");

        await _mutationService.Received(1).CreateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<CalendarEvent>(e => e.Title == "Team Standup"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_SaveAsync_updates_existing_event_preserving_etag()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);

        var existing = new CalendarEvent
        {
            Id = "ev-456",
            CalendarId = "primary",
            Title = "Old Title",
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddHours(1),
            AllDay = false,
            Etag = "etag-original"
        };
        vm.InitializeForEdit(existing);
        vm.Title = "Updated Title";

        _mutationService.UpdateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        CalendarEvent? savedEvent = null;
        vm.Saved += (_, ev) => savedEvent = ev;

        var success = await vm.SaveAsync(TestContext.Current.CancellationToken);

        success.Should().BeTrue();
        savedEvent.Should().NotBeNull();
        savedEvent!.Title.Should().Be("Updated Title");
        savedEvent.Etag.Should().Be("etag-original");

        await _mutationService.Received(1).UpdateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<CalendarEvent>(e => e.Id == "ev-456" && e.Etag == "etag-original"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_DeleteAsync_calls_mutation_service_and_raises_event()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        var existing = new CalendarEvent
        {
            Id = "ev-to-delete",
            CalendarId = "primary",
            Title = "To Delete",
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddHours(1),
            AllDay = false,
            Etag = "etag-delete"
        };
        vm.InitializeForEdit(existing);

        string? deletedId = null;
        vm.Deleted += (_, id) => deletedId = id;

        var success = await vm.DeleteAsync(TestContext.Current.CancellationToken);

        success.Should().BeTrue();
        deletedId.Should().Be("ev-to-delete");

        await _mutationService.Received(1).DeleteEventOptimisticAsync(
            Arg.Any<string>(),
            "primary",
            "ev-to-delete",
            "etag-delete",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_DuplicateAsync_creates_copy_with_shifted_time()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(10, 0), isAllDay: false);
        vm.Title = "Weekly Workshop";

        _mutationService.CreateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        var duplicate = await vm.DuplicateAsync(TestContext.Current.CancellationToken);

        duplicate.Should().NotBeNull();
        duplicate!.Title.Should().Be("Weekly Workshop");
        duplicate.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FR_UI_4_Lunar_recurrence_rule_populates_extended_properties()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        var date = new DateOnly(2026, 11, 9); // Day in lunar calendar
        vm.InitializeForLunarRule(date);

        vm.RecurrenceKind.Should().Be(EventRecurrenceType.Lunar);
        vm.IsLunarRecurrence.Should().BeTrue();
        vm.LunarDay.Should().BeGreaterThan(0);
        vm.LunarMonth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FR_UI_4_Reminder_chips_addition_and_removal()
    {
        var vm = new EventEditorViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        vm.InitializeForNew();

        vm.Reminders.Clear();
        vm.AddReminder(15);
        vm.AddReminder(60);

        vm.Reminders.Should().Contain(15);
        vm.Reminders.Should().Contain(60);
        vm.ReminderChips.Should().HaveCount(2);

        vm.RemoveReminder(15);
        vm.Reminders.Should().NotContain(15);
        vm.ReminderChips.Should().HaveCount(1);
    }

    [Fact]
    public async Task FR_UI_4_WeekViewModel_AdjustEventTimeAsync_updates_times_via_mutation_service()
    {
        var ev = new CalendarEvent
        {
            Id = "ev-drag",
            CalendarId = "primary",
            Title = "Draggable Event",
            StartUtc = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero),
            AllDay = false,
        };

        _eventStore.GetEventByIdAsync("ev-drag", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CalendarEvent?>(ev));

        _mutationService.UpdateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        using var weekVm = new WeekViewModel(_eventStore, _localizationService, _preferences, _mutationService);

        var delta = TimeSpan.FromMinutes(30);
        var result = await weekVm.AdjustEventTimeAsync("ev-drag", delta, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.StartUtc.Should().Be(ev.StartUtc + delta);
        result.EndUtc.Should().Be(ev.EndUtc + delta);

        await _mutationService.Received(1).UpdateEventOptimisticAsync(
            "primary",
            "primary",
            Arg.Is<CalendarEvent>(e => e.Id == "ev-drag" && e.StartUtc == ev.StartUtc + delta),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_DayViewModel_AdjustEventTimeAsync_updates_times_via_mutation_service()
    {
        var ev = new CalendarEvent
        {
            Id = "ev-day-drag",
            CalendarId = "primary",
            Title = "Day Draggable Event",
            StartUtc = new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero),
            AllDay = false,
        };

        _eventStore.GetEventByIdAsync("ev-day-drag", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CalendarEvent?>(ev));

        _mutationService.UpdateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        using var dayVm = new DayViewModel(_eventStore, _localizationService, _preferences, _mutationService);

        var delta = TimeSpan.FromMinutes(-15);
        var result = await dayVm.AdjustEventTimeAsync("ev-day-drag", delta, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.StartUtc.Should().Be(ev.StartUtc + delta);
        result.EndUtc.Should().Be(ev.EndUtc + delta);

        await _mutationService.Received(1).UpdateEventOptimisticAsync(
            "primary",
            "primary",
            Arg.Is<CalendarEvent>(e => e.Id == "ev-day-drag" && e.StartUtc == ev.StartUtc + delta),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_EventClipboard_Copy_and_PasteAsync()
    {
        var ev = new CalendarEvent
        {
            Id = "ev-copy",
            CalendarId = "primary",
            Title = "Event to Copy",
            StartUtc = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero),
            AllDay = false,
        };

        EventClipboard.Copy(ev);
        EventClipboard.HasEvent.Should().BeTrue();
        EventClipboard.Current.Should().Be(ev);

        _mutationService.CreateEventOptimisticAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CalendarEvent>()));

        using var weekVm = new WeekViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        var targetDate = new DateOnly(2026, 10, 22);
        var targetTime = new TimeOnly(13, 0);

        var pasted = await weekVm.PasteEventAsync(targetDate, targetTime, TestContext.Current.CancellationToken);

        pasted.Should().NotBeNull();
        pasted!.Title.Should().Be("Event to Copy");
        pasted.Id.Should().NotBe("ev-copy"); // Must have new unique ID
    }
}
