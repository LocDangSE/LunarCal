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
/// Every calendar view buckets events into days in the time zone selected in settings, not in a fixed offset
/// or the machine zone (FR-UI-7, FR-I18N-4).
/// </summary>
public sealed class TimeZoneBucketingTests
{
    private static readonly TimeZoneInfo _utcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    // 16 Nov 2026 01:00–02:00 at UTC+7 is 15 Nov 2026 13:00–14:00 at UTC-5.
    private static readonly CalendarEvent _lateEvent = new()
    {
        Id = "late",
        CalendarId = "cal",
        Title = "Late call",
        StartUtc = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero),
        EndUtc = new DateTimeOffset(2026, 11, 15, 19, 0, 0, TimeSpan.Zero),
    };

    private static readonly DateOnly _nov15 = new(2026, 11, 15);
    private static readonly DateOnly _nov16 = new(2026, 11, 16);

    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();
    private readonly LocalizationService _localizationService = new(SupportedCultures.Vietnamese);
    private readonly TestDisplayPreferences _preferences = new();

    public TimeZoneBucketingTests()
    {
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(new[] { _lateEvent }));
        _eventStore.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(new[]
            {
                new CalendarInfo { Id = "cal", AccountId = "acc", Summary = "Cal", Color = "#123456" },
            }));
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(true, 15)]
    public async Task FR_UI_7_Events_bucketed_by_selected_timezone_in_month_view(bool useUtcMinus5, int expectedDay)
    {
        SelectZone(useUtcMinus5);
        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.PopulateMonthAsync(new DateOnly(2026, 11, 1));

        var daysWithEvent = vm.Days.Where(d => d.Events.Count > 0).Select(d => d.SolarDate).ToList();
        daysWithEvent.Should().Equal(new DateOnly(2026, 11, expectedDay));
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(true, 15)]
    public async Task FR_UI_7_Events_bucketed_by_selected_timezone_in_week_view(bool useUtcMinus5, int expectedDay)
    {
        SelectZone(useUtcMinus5);
        _preferences.WeekStart = DayOfWeek.Sunday; // week of 15–21 Nov contains both candidate days
        using var vm = new WeekViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(_nov15);

        var columnsWithEvent = vm.DayColumns.Where(c => c.TimedEvents.Count > 0).Select(c => c.Date).ToList();
        columnsWithEvent.Should().Equal(new DateOnly(2026, 11, expectedDay));

        var timed = vm.DayColumns.Single(c => c.TimedEvents.Count > 0).TimedEvents.Single();
        timed.TimeRangeText.Should().Be(useUtcMinus5 ? "13:00 – 14:00" : "01:00 – 02:00");
        timed.Top.Should().Be((useUtcMinus5 ? 13 : 1) * DayColumnViewModel.HourRowHeight);
    }

    [Fact]
    public async Task FR_UI_7_Events_bucketed_by_selected_timezone_in_day_view()
    {
        SelectZone(useUtcMinus5: true);
        using var vm = new DayViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(_nov15);

        vm.DayColumn!.TimedEvents.Should().ContainSingle()
            .Which.TimeRangeText.Should().Be("13:00 – 14:00");

        // The store is queried from local midnight to local midnight in UTC-5.
        await _eventStore.Received().GetEventsAsync(
            new DateTimeOffset(2026, 11, 15, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        // Same event is not on 16 Nov in UTC-5.
        await vm.SetDisplayDateAsync(_nov16);
        vm.DayColumn!.TimedEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, 16, "01:00 – 02:00")]
    [InlineData(true, 15, "13:00 – 14:00")]
    public async Task FR_UI_7_Events_bucketed_by_selected_timezone_in_agenda_view(bool useUtcMinus5, int expectedDay, string expectedTime)
    {
        SelectZone(useUtcMinus5);
        using var vm = new AgendaViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(_nov15);

        var group = vm.DayGroups.Should().ContainSingle().Subject;
        group.Date.Should().Be(new DateOnly(2026, 11, expectedDay));
        group.Events.Single().TimeDisplay.Should().Be(expectedTime);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task FR_UI_7_Events_bucketed_by_selected_timezone_in_day_detail(bool useUtcMinus5, bool expectEventOnNov15)
    {
        SelectZone(useUtcMinus5);
        using var vm = new DayDetailViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDateAsync(_nov15, TestContext.Current.CancellationToken);

        vm.HasEvents.Should().Be(expectEventOnNov15);
        if (expectEventOnNov15)
        {
            vm.Events.Single().TimeDisplay.Should().Be("13:00 – 14:00");
        }
    }

    [Fact]
    public async Task FR_I18N_4_Event_times_follow_12h_setting()
    {
        _preferences.TimeFormat = TimeFormattingHelper.Format12Hours;
        _localizationService.SetCulture(SupportedCultures.English);
        using var vm = new AgendaViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(_nov15);

        vm.DayGroups.Single().Events.Single().TimeDisplay.Should().Be("1:00 AM – 2:00 AM");
    }

    private void SelectZone(bool useUtcMinus5)
    {
        _preferences.TimeZone = useUtcMinus5 ? _utcMinus5 : TestDisplayPreferences.UtcPlus7;
    }
}
