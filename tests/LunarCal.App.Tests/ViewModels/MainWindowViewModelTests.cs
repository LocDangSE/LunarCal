using System.Globalization;
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

public sealed class MainWindowViewModelTests
{
    private readonly IEventStore _eventStore;
    private readonly LocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly ISettingsStore _settingsStore;
    private readonly TestDisplayPreferences _preferences = new();

    public MainWindowViewModelTests()
    {
        _eventStore = Substitute.For<IEventStore>();
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new List<CalendarInfo>
            {
                new() { Id = "cal-1", AccountId = "acc-1", Summary = "Cá nhân", Color = "#4285F4", IsSelected = true, IsPrimary = true },
                new() { Id = "cal-2", AccountId = "acc-1", Summary = "Công việc", Color = "#0B8043", IsSelected = true },
            }));

        _localizationService = new LocalizationService(SupportedCultures.Vietnamese);
        _themeService = Substitute.For<IThemeService>();
        _settingsStore = Substitute.For<ISettingsStore>();
    }

    [Fact]
    public void FR_UI_9_GoToToday_sets_selected_date_to_today()
    {
        using var vm = CreateViewModel();

        // Move to another date
        vm.SelectedDate = new DateTime(2025, 5, 20);
        vm.SelectedDate.Should().NotBe(DateTime.Today);

        // Execute GoToToday
        vm.GoToTodayCommand.Execute(null);

        vm.SelectedDate.Date.Should().Be(DateTime.Today);
        vm.MiniCalendar.SelectedDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public void FR_UI_9_PreviousPeriod_and_NextPeriod_navigate_according_to_active_view()
    {
        using var vm = CreateViewModel();
        var baseline = new DateTime(2026, 11, 15);
        vm.SelectedDate = baseline;

        // 1. Month View
        vm.ActiveViewMode = CalendarViewMode.Month;
        vm.NextPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline.AddMonths(1));
        vm.PreviousPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline);

        // 2. Week View
        vm.ActiveViewMode = CalendarViewMode.Week;
        vm.NextPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline.AddDays(7));
        vm.PreviousPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline);

        // 3. Day View
        vm.ActiveViewMode = CalendarViewMode.Day;
        vm.NextPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline.AddDays(1));
        vm.PreviousPeriodCommand.Execute(null);
        vm.SelectedDate.Should().Be(baseline);
    }

    [Fact]
    public void FR_UI_9_ChangeView_updates_ActiveViewMode()
    {
        using var vm = CreateViewModel();

        vm.ActiveViewMode.Should().Be(CalendarViewMode.Month);

        vm.ChangeViewCommand.Execute(CalendarViewMode.Week);
        vm.ActiveViewMode.Should().Be(CalendarViewMode.Week);

        vm.ChangeViewCommand.Execute(CalendarViewMode.Day);
        vm.ActiveViewMode.Should().Be(CalendarViewMode.Day);

        vm.ChangeViewCommand.Execute(CalendarViewMode.Agenda);
        vm.ActiveViewMode.Should().Be(CalendarViewMode.Agenda);
    }

    [Fact]
    public void FR_UI_9_SwitchLanguage_updates_culture_immediately_without_restart()
    {
        using var vm = CreateViewModel();

        // Switch to English
        vm.SwitchLanguageCommand.Execute("en-US");

        vm.CurrentLanguage.Should().Be("en-US");
        vm.IsEnglishActive.Should().BeTrue();
        vm.IsVietnameseActive.Should().BeFalse();
        _localizationService.CurrentCulture.Name.Should().Be("en-US");
        vm.HeaderTitle.Should().Contain("November");

        // Switch back to Vietnamese
        vm.SwitchLanguageCommand.Execute("vi-VN");

        vm.CurrentLanguage.Should().Be("vi-VN");
        vm.IsVietnameseActive.Should().BeTrue();
        vm.IsEnglishActive.Should().BeFalse();
        _localizationService.CurrentCulture.Name.Should().Be("vi-VN");
        vm.HeaderTitle.Should().StartWith("Tháng");
    }

    [Fact]
    public void FR_LUNAR_6_Lunar_subtitle_computes_from_Core_engine_and_hides_when_disabled()
    {
        using var vm = CreateViewModel();
        vm.SelectedDate = new DateTime(2026, 11, 1);

        // Visible by default
        vm.IsLunarVisible.Should().BeTrue();
        vm.LunarHeaderSubtitle.Should().NotBeNullOrWhiteSpace();
        vm.LunarHeaderSubtitle.Should().Contain("Bính Ngọ");

        // When lunar is disabled (FR-LUNAR-6), subtitle is hidden
        vm.IsLunarVisible = false;
        vm.GoToTodayCommand.Execute(null);
        vm.LunarHeaderSubtitle.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_UI_10_Calendars_loaded_and_toggled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var vm = CreateViewModel();

        await vm.LoadCalendarsAsync(ct);

        vm.Calendars.Should().HaveCount(2);
        var cal = vm.Calendars[0];
        cal.IsSelected.Should().BeTrue();

        vm.ToggleCalendarCommand.Execute(cal);
        cal.IsSelected.Should().BeFalse();

        vm.ToggleCalendarCommand.Execute(cal);
        cal.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void FR_UI_10_MiniCalendar_selection_updates_main_window_selected_date()
    {
        using var vm = CreateViewModel();
        var targetDate = new DateOnly(2026, 12, 25);

        var dayItem = new MiniCalendarDayViewModel
        {
            Date = targetDate,
            DayNumber = 25,
            IsCurrentMonth = true,
        };

        vm.MiniCalendar.SelectDayCommand.Execute(dayItem);

        vm.SelectedDate.Date.Should().Be(new DateTime(2026, 12, 25));
        vm.MiniCalendar.SelectedDate.Should().Be(targetDate);
    }

    [Fact]
    public void FR_UI_7_Week_start_setting_survives_language_switch()
    {
        _preferences.WeekStart = DayOfWeek.Sunday;
        using var vm = CreateViewModel();

        vm.SwitchLanguageCommand.Execute("vi-VN");
        vm.SwitchLanguageCommand.Execute("en-US");
        vm.SwitchLanguageCommand.Execute("vi-VN");

        // Vietnamese would default to Monday, but the user's Sunday setting must win (FR-UI-7, FR-I18N-4).
        vm.MonthView.WeekStart.Should().Be(DayOfWeek.Sunday);
        vm.WeekView.WeekStart.Should().Be(DayOfWeek.Sunday);
        vm.MiniCalendar.DayHeaders[0].Should().Be("CN");
    }

    [Fact]
    public void FR_UI_7_Changing_week_start_preference_updates_every_view_immediately()
    {
        _preferences.WeekStart = DayOfWeek.Monday;
        using var vm = CreateViewModel();
        vm.SelectedDate = new DateTime(2026, 11, 18); // Wednesday
        vm.ChangeViewCommand.Execute(CalendarViewMode.Week);

        _preferences.WeekStart = DayOfWeek.Sunday;

        vm.MonthView.WeekStart.Should().Be(DayOfWeek.Sunday);
        vm.MonthView.WeekdayHeaders[0].Should().Be("CN");
        vm.WeekView.WeekStart.Should().Be(DayOfWeek.Sunday);
        vm.MiniCalendar.DayHeaders[0].Should().Be("CN");
        vm.HeaderTitle.Should().StartWith("15 – 21");
    }

    [Fact]
    public void FR_LUNAR_6_Lunar_preference_change_hides_lunar_text_in_all_views()
    {
        using var vm = CreateViewModel();
        vm.IsLunarVisible.Should().BeTrue();

        _preferences.LunarEnabled = false;

        vm.IsLunarVisible.Should().BeFalse();
        vm.LunarHeaderSubtitle.Should().BeEmpty();
        vm.MonthView.IsLunarVisible.Should().BeFalse();
        vm.WeekView.IsLunarVisible.Should().BeFalse();
        vm.DayView.IsLunarVisible.Should().BeFalse();
        vm.AgendaView.IsLunarVisible.Should().BeFalse();
    }

    [Fact]
    public void FR_I18N_4_Time_format_preference_change_reformats_event_times()
    {
        var day = new DateOnly(2026, 11, 18);
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(new[]
            {
                new CalendarEvent
                {
                    Id = "e1",
                    CalendarId = "cal-1",
                    Title = "Meeting",
                    StartUtc = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(7)),
                    EndUtc = new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.FromHours(7)),
                },
            }));
        _localizationService.SetCulture(SupportedCultures.English);
        using var vm = CreateViewModel();
        vm.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
        _preferences.TimeFormat = TimeFormattingHelper.Format24Hours;
        _ = vm.DayView.SetDisplayDateAsync(day);
        _ = vm.AgendaView.SetDisplayDateAsync(day);
        vm.DayView.DayColumn!.TimedEvents.Single().TimeRangeText.Should().Be("09:00 – 10:30");

        _preferences.TimeFormat = TimeFormattingHelper.Format12Hours;

        vm.DayView.DayColumn!.TimedEvents.Single().TimeRangeText.Should().Be("9:00 AM – 10:30 AM");
        vm.AgendaView.DayGroups.Single().Events.Single().TimeDisplay.Should().Be("9:00 AM – 10:30 AM");
    }

    [Fact]
    public void FR_UI_7_OpenSettings_raises_settings_requested()
    {
        using var vm = CreateViewModel();
        var raised = 0;
        vm.SettingsRequested += (_, _) => raised++;

        vm.OpenSettingsCommand.Execute(null);

        raised.Should().Be(1);
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_eventStore, _localizationService, _themeService, _settingsStore, _preferences);
    }
}
