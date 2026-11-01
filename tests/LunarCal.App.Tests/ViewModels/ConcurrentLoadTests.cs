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
/// Overlapping loads (fire-and-forget refreshes after a setting or language change) must not let an older,
/// slower store call overwrite the result of a newer one (FR-UI-7, AGENTS.md §7).
/// </summary>
public sealed class ConcurrentLoadTests
{
    private readonly IEventStore _eventStore = Substitute.For<IEventStore>();
    private readonly LocalizationService _localizationService = new(SupportedCultures.Vietnamese);
    private readonly TestDisplayPreferences _preferences = new();

    [Fact]
    public async Task FR_UI_7_Second_week_start_change_wins_even_if_first_load_finishes_last()
    {
        var empty = Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>());
        var first = new TaskCompletionSource<IReadOnlyList<CalendarEvent>>();
        var second = new TaskCompletionSource<IReadOnlyList<CalendarEvent>>();
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(empty);

        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(first.Task, second.Task);

        vm.SetWeekStart(DayOfWeek.Saturday); // load #1 (slow)
        vm.SetWeekStart(DayOfWeek.Sunday);   // load #2

        second.SetResult(Array.Empty<CalendarEvent>());
        first.SetResult(Array.Empty<CalendarEvent>());
        await Task.Yield();

        vm.WeekStart.Should().Be(DayOfWeek.Sunday);
        vm.WeekdayHeaders[0].Should().Be("CN");
        vm.Days.Should().HaveCount(42);
        vm.Days[0].SolarDate.DayOfWeek.Should().Be(DayOfWeek.Sunday, "the grid of the last request must be kept");
    }

    [Fact]
    public async Task FR_UI_1_Store_failure_during_load_shows_empty_grid_instead_of_crashing()
    {
        _eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<CalendarEvent>>(new InvalidOperationException("store offline")));

        using var vm = new MonthViewModel(_eventStore, _localizationService, _preferences);
        await vm.SetDisplayDateAsync(new DateOnly(2026, 11, 1));

        vm.Days.Should().HaveCount(42);
        vm.Days.Should().OnlyContain(d => d.Events.Count == 0);
    }
}
