using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// Event editor behaviour behind the rebuilt Figma S10 dialog (P3-R2, P3-REVIEW): reminders and solar recurrence reach
/// the saved event, the lunar-rule panel texts, the segmented recurrence control, and the calendar requirement.
/// </summary>
public sealed class EventEditorS10Tests
{
    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();
    private readonly IMutationService _mutationService = Substitute.For<IMutationService>();
    private readonly LocalizationService _localization = new(SupportedCultures.Vietnamese);
    private readonly TestDisplayPreferences _preferences = new();

    public EventEditorS10Tests()
    {
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new List<CalendarInfo>
            {
                new() { Id = "work", AccountId = "acc", Summary = "Công việc", Color = "#0B8043", IsPrimary = false, IsSelected = true },
                new() { Id = "personal", AccountId = "acc", Summary = "Cá nhân", Color = "#2F6BFF", IsPrimary = true, IsSelected = true },
            }));
        _mutationService.CreateEventOptimisticAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<CalendarEvent>()));
        _mutationService.UpdateEventOptimisticAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<CalendarEvent>()));
    }

    private EventEditorViewModel CreateVm() => new(_eventStore, _localization, _preferences, _mutationService);

    private CalendarEvent LastWritten(string method)
    {
        var call = _mutationService.ReceivedCalls().Last(c => c.GetMethodInfo().Name == method);
        return (CalendarEvent)call.GetArguments()[2]!;
    }

    [Fact]
    public async Task FR_UI_4_Save_writes_the_chosen_reminders_and_solar_recurrence()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0));
        vm.Title = "Họp nhóm";
        vm.AddReminder(1440);
        vm.RemoveReminder(10);
        vm.IsSolarRecurrence = true;

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        var written = LastWritten(nameof(IMutationService.CreateEventOptimisticAsync));
        written.ReminderMinutes.Should().Equal(1440);
        written.Recurrence.Should().Equal("RRULE:FREQ=WEEKLY");
    }

    [Fact]
    public async Task FR_UI_4_Removing_every_reminder_saves_an_empty_list_not_the_calendar_default()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0));
        vm.Title = "Không nhắc";
        vm.RemoveReminder(10);

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        LastWritten(nameof(IMutationService.CreateEventOptimisticAsync)).ReminderMinutes.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_UI_4_Editing_an_event_on_calendar_default_reminders_keeps_them_default()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForEdit(Existing(reminders: null, recurrence: null));
        vm.Title = "Đổi tên";

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        // null = reminders.useDefault; an empty list would silently switch the event's reminders off.
        LastWritten(nameof(IMutationService.UpdateEventOptimisticAsync)).ReminderMinutes.Should().BeNull();
    }

    [Fact]
    public async Task FR_UI_4_Editing_shows_the_events_own_reminders_as_chips_latest_first()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForEdit(Existing(reminders: [120, 1440], recurrence: null));

        vm.ReminderChips.Select(c => c.Label).Should().Equal("Thông báo · 1 ngày trước", "Thông báo · 2 giờ trước");
        vm.ReminderPresets.Select(p => p.Minutes).Should().NotContain([120, 1440]).And.Contain(10);
    }

    [Fact]
    public async Task FR_UI_4_Editing_a_recurring_master_keeps_a_rule_the_editor_cannot_express()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        string[] rule = ["RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10"];
        vm.InitializeForEdit(Existing(reminders: [30], recurrence: rule));

        vm.IsSolarRecurrence.Should().BeTrue();
        vm.SelectedSolarFrequencyOption!.Type.Should().Be(EventRecurrenceType.Weekly);

        vm.Title = "Đổi tên";
        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.UpdateEventOptimisticAsync)).Recurrence.Should().Equal(rule);

        // Changing the frequency is a deliberate edit: the original BYDAY/COUNT no longer apply.
        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Monthly);
        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.UpdateEventOptimisticAsync)).Recurrence.Should().Equal("RRULE:FREQ=MONTHLY");
    }

    [Fact]
    public async Task FR_UI_4_Switching_a_recurring_master_to_no_repeat_removes_its_rule()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForEdit(Existing(reminders: [30], recurrence: ["RRULE:FREQ=DAILY"]));

        vm.IsRecurrenceNone = true;
        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        LastWritten(nameof(IMutationService.UpdateEventOptimisticAsync)).Recurrence.Should().BeNull();
    }

    [Fact]
    public async Task FR_UI_4_Save_without_a_calendar_is_refused_instead_of_writing_to_a_made_up_primary()
    {
        // Before the fix the editor never loaded calendars (MainWindow.OnEventEditorRequested) and fell back to
        // calendar/account "primary", which has no CalendarInfo row, so the Event insert broke the foreign key.
        var vm = CreateVm();
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0));
        vm.Title = "Không có lịch";

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeFalse();

        vm.ErrorMessage.Should().Be(_localization["event.validation.calendar_required"]);
        await _mutationService.DidNotReceive().CreateEventOptimisticAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_New_event_defaults_to_the_primary_calendar_of_its_account()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0));
        vm.Title = "Mặc định";

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        var written = LastWritten(nameof(IMutationService.CreateEventOptimisticAsync));
        written.CalendarId.Should().Be("personal");
        await _mutationService.Received(1).CreateEventOptimisticAsync("acc", "personal", Arg.Any<CalendarEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_4_Segmented_recurrence_control_maps_to_the_recurrence_kind()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));

        vm.IsRecurrenceNone.Should().BeTrue();

        vm.IsSolarRecurrence = true;
        vm.RecurrenceKind.Should().Be(EventRecurrenceType.Weekly);
        vm.IsRecurrenceNone.Should().BeFalse();

        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Yearly);
        vm.RecurrenceKind.Should().Be(EventRecurrenceType.Yearly);

        vm.IsLunarRecurrence = true;
        vm.RecurrenceKind.Should().Be(EventRecurrenceType.Lunar);
        vm.IsSolarRecurrence.Should().BeFalse();

        // Back to solar remembers the last solar frequency.
        vm.IsSolarRecurrence = true;
        vm.RecurrenceKind.Should().Be(EventRecurrenceType.Yearly);
    }

    [Fact]
    public async Task FR_LUNAR_5_Lunar_rule_panel_shows_headline_policy_and_next_three_dates()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));
        vm.IsLunarRecurrence = true;

        // 22/10/2026 is lunar 13/9 (Figma S10 sample); the next years come from the lunar engine.
        vm.LunarRuleTitle.Should().Be("Hằng năm vào ngày 13 tháng 9 âm lịch");
        vm.LunarRulePolicyText.Should().Be("Năm có tháng 9 nhuận: chỉ tháng thường · Tháng thiếu: dùng ngày cuối tháng · Tạo trước 5 năm");
        vm.LunarRuleNextText.Should().Be("Lần tới: 22/10/2026 · 12/10/2027 · 30/10/2028");
        vm.StartLunarChipText.Should().Be("13/9 ÂL");
    }

    [Fact]
    public async Task FR_LUNAR_5_Lunar_rule_follows_the_start_date_until_it_is_customised()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));
        vm.IsLunarRecurrence = true;

        vm.StartDate = new DateOnly(2026, 11, 9); // lunar 1/10
        vm.LunarDay.Should().Be(1);
        vm.LunarMonth.Should().Be(10);
        vm.LunarRuleTitle.Should().Be("Hằng năm vào ngày 1 tháng 10 âm lịch");

        vm.ToggleLunarRuleCustomizingCommand.Execute(null);
        vm.IsLunarRuleCustomizing.Should().BeTrue();
        vm.LunarDay = 15;
        vm.StartDate = new DateOnly(2026, 11, 20);
        vm.LunarDay.Should().Be(15, "a customised rule must not be overwritten by a date change");
    }

    [Fact]
    public async Task FR_LUNAR_5_Day_30_of_a_short_lunar_month_falls_back_to_the_last_day()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));
        vm.IsLunarRecurrence = true;
        vm.ToggleLunarRuleCustomizingCommand.Execute(null);
        vm.LunarMonth = 2;
        vm.LunarDay = 30;

        // Whatever the length of lunar month 2 is, every listed date must exist, and there must be three.
        var next = vm.LunarRuleNextText;
        next.Should().StartWith("Lần tới: ");
        next[("Lần tới: ".Length)..].Split(" · ").Should().HaveCount(3);
    }

    [Fact]
    public async Task FR_UI_4_End_date_picker_is_shown_only_for_all_day_or_multi_day_events()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));

        vm.ShowEndDate.Should().BeFalse();
        vm.IsAllDay = true;
        vm.ShowEndDate.Should().BeTrue();
        vm.IsAllDay = false;
        vm.EndDate = new DateOnly(2026, 10, 23);
        vm.ShowEndDate.Should().BeTrue();
    }

    [Fact]
    public async Task FR_UI_4_Header_sub_line_names_the_calendar_and_the_last_edit()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForEdit(Existing(reminders: [30], recurrence: null) with { Updated = DateTimeOffset.UtcNow.AddMinutes(-2) });

        vm.DialogSubtitle.Should().Be("Công việc · sửa lần cuối 2 phút trước");
    }

    [Fact]
    public async Task FR_UI_4_Time_zone_text_shows_the_offset_of_the_display_time_zone()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(10, 0));

        vm.TimeZoneText.Should().StartWith("(GMT+07:00)");
    }

    [Fact]
    public void FR_UI_4_Time_slots_are_half_hours_across_the_day()
    {
        var vm = CreateVm();

        vm.TimeSlots.Should().HaveCount(48);
        vm.TimeSlots[0].Should().Be("00:00");
        vm.TimeSlots[1].Should().Be("00:30");
        vm.TimeSlots[^1].Should().Be("23:30");
    }

    private static CalendarEvent Existing(IReadOnlyList<int>? reminders, IReadOnlyList<string>? recurrence) => new()
    {
        Id = "ev-1",
        GoogleId = "g-1",
        CalendarId = "work",
        Title = "Sự kiện",
        StartUtc = new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero),
        EndUtc = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero),
        Etag = "etag-1",
        Status = "confirmed",
        ReminderMinutes = reminders,
        Recurrence = recurrence,
    };

    [Fact]
    public async Task FR_UI_4_Overnight_event_22_to_01_becomes_next_day_shows_chip_and_allows_custom_end_date()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(22, 0));

        // When setting EndTime to 01:00 (which is <= StartTime 22:00)
        vm.EndTime = new TimeOnly(1, 0);

        // System automatically understands it as next day (+1 day)
        vm.EndDate.Should().Be(new DateOnly(2026, 11, 11));
        vm.IsOvernight.Should().BeTrue();
        vm.OvernightChipText.Should().Be("+1 ngày · kết thúc 01:00");
        vm.ShowEndDate.Should().BeFalse("overnight chip clarifies the date without needing full picker");

        // Clicking 'Ngày kết thúc khác…' reveals the end date picker
        vm.OpenCustomEndDateCommand.Execute(null);
        vm.IsCustomEndDateOpen.Should().BeTrue();
        vm.ShowEndDate.Should().BeTrue();

        // User can adjust end date further (e.g. 2 days later)
        vm.EndDate = new DateOnly(2026, 11, 12);
        vm.OvernightChipText.Should().Be("+2 ngày · kết thúc 01:00");

        // Saving succeeds and stores correct start and end UTC
        vm.Title = "Ca trực đêm";
        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        var written = LastWritten(nameof(IMutationService.CreateEventOptimisticAsync));
        written.StartUtc.Should().Be(CalendarTime.ToUtc(new DateOnly(2026, 11, 10), new TimeOnly(22, 0), _preferences.TimeZone));
        written.EndUtc.Should().Be(CalendarTime.ToUtc(new DateOnly(2026, 11, 12), new TimeOnly(1, 0), _preferences.TimeZone));
    }

    [Fact]
    public async Task FR_UI_4_Per_event_timezone_is_persisted_and_used_for_utc_conversion()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 11, 10), new TimeOnly(9, 0));
        vm.Title = "Họp quốc tế";

        // Default option is "Theo Cài đặt"
        vm.SelectedTimeZoneOption.Should().NotBeNull();
        vm.SelectedTimeZoneOption!.IsDefault.Should().BeTrue();

        // Select UTC
        var utcOption = vm.TimeZoneOptions.First(o => o.Id == "UTC");
        vm.SelectedTimeZoneOption = utcOption;
        vm.SelectedTimeZoneInfo.Should().Be(TimeZoneInfo.Utc);

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        var written = LastWritten(nameof(IMutationService.CreateEventOptimisticAsync));
        written.TimeZone.Should().Be("UTC");
        // In UTC, 9:00 is 9:00 UTC
        written.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        written.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));

        // When initializing for edit on an event with specific timezone, that timezone is selected
        var editVm = CreateVm();
        await editVm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        editVm.InitializeForEdit(written);

        editVm.SelectedTimeZoneOption!.Id.Should().Be("UTC");
        editVm.StartTime.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task FR_UI_4_Solar_recurrence_options_produce_correct_rrule_and_next_occurrences()
    {
        var vm = CreateVm();
        await vm.LoadCalendarsAsync(TestContext.Current.CancellationToken);
        vm.InitializeForNew(new DateOnly(2026, 10, 22), new TimeOnly(9, 0)); // 2026-10-22 is Thursday
        vm.Title = "Lặp tuần";

        vm.IsSolarRecurrence = true;

        // 1. Weekly (default solar frequency)
        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Weekly);
        vm.SolarRuleTitle.Should().ContainEquivalentOf("thứ Năm");
        vm.SolarRuleNextText.Should().Be("Lần tới: 29/10/2026 · 05/11/2026 · 12/11/2026");

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.CreateEventOptimisticAsync)).Recurrence.Should().Equal("RRULE:FREQ=WEEKLY");

        // 2. Daily
        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Daily);
        vm.SolarRuleTitle.Should().Be("Hằng ngày");
        vm.SolarRuleNextText.Should().Be("Lần tới: 23/10/2026 · 24/10/2026 · 25/10/2026");

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.CreateEventOptimisticAsync)).Recurrence.Should().Equal("RRULE:FREQ=DAILY");

        // 3. Monthly
        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Monthly);
        vm.SolarRuleTitle.Should().Be("Lặp vào ngày 22 hàng tháng");
        vm.SolarRuleNextText.Should().Be("Lần tới: 22/11/2026 · 22/12/2026 · 22/01/2027");

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.CreateEventOptimisticAsync)).Recurrence.Should().Equal("RRULE:FREQ=MONTHLY");

        // 4. Yearly
        vm.SelectedSolarFrequencyOption = vm.SolarFrequencyOptions.Single(o => o.Type == EventRecurrenceType.Yearly);
        vm.SolarRuleTitle.Should().Be("Hằng năm vào ngày 22 tháng 10");
        vm.SolarRuleNextText.Should().Be("Lần tới: 22/10/2027 · 22/10/2028 · 22/10/2029");

        (await vm.SaveAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        LastWritten(nameof(IMutationService.CreateEventOptimisticAsync)).Recurrence.Should().Equal("RRULE:FREQ=YEARLY");
    }
}
