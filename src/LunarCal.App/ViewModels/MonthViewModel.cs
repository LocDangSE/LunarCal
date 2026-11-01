using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the 6×7 month grid calendar view (FR-UI-1, FR-UI-2, FR-UI-7, FR-LUNAR-6, NFR-PERF).
/// </summary>
public sealed partial class MonthViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly IDisplayPreferences _preferences;

    // Incremented by every load request; a load whose version is no longer current drops its result, so the
    // latest request always wins even when earlier store calls complete later.
    private int _loadVersion;

    [ObservableProperty]
    private DateOnly _displayDate;

    [ObservableProperty]
    private MonthDayViewModel? _selectedDay;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private DayOfWeek _weekStart = DayOfWeek.Monday;

    [ObservableProperty]
    private string _monthTitle = string.Empty;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    /// <summary>Fired when a day is selected in the month view.</summary>
    public event EventHandler<DateOnly>? DaySelected;

    /// <summary>Day of week header strings (7 columns).</summary>
    public ObservableCollection<string> WeekdayHeaders { get; } = new();

    /// <summary>The 42 day cells in the 6×7 calendar grid.</summary>
    public ObservableCollection<MonthDayViewModel> Days { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="MonthViewModel"/>.
    /// </summary>
    public MonthViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        IDisplayPreferences preferences)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        var today = TodayInDisplayZone();
        _displayDate = new DateOnly(today.Year, today.Month, 1);
        _isLunarVisible = _preferences.LunarEnabled;
        _weekStart = _preferences.WeekStart;

        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAsync();
    }

    /// <summary>
    /// Applies week start and lunar visibility from the display preferences and rebuilds the grid (FR-UI-7, FR-LUNAR-6).
    /// </summary>
    public async Task InitializeSettingsAsync()
    {
        IsLunarVisible = _preferences.LunarEnabled;
        WeekStart = _preferences.WeekStart;

        RebuildWeekdayHeaders();
        await PopulateMonthAsync(DisplayDate);
    }

    /// <summary>
    /// Sets the week start day and rebuilds headers and grid.
    /// </summary>
    public void SetWeekStart(DayOfWeek weekStart)
    {
        WeekStart = weekStart;
        RebuildWeekdayHeaders();
        _ = PopulateMonthAsync(DisplayDate);
    }

    /// <summary>
    /// Updates lunar visibility across the month view and all day cells (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        foreach (var day in Days)
        {
            day.SetLunarVisibility(visible);
        }
    }

    /// <summary>
    /// Navigates the month view to the specified date.
    /// </summary>
    public async Task SetDisplayDateAsync(DateOnly date)
    {
        DisplayDate = new DateOnly(date.Year, date.Month, 1);
        await PopulateMonthAsync(DisplayDate);
    }

    [RelayCommand]
    public async Task PreviousMonth()
    {
        DisplayDate = DisplayDate.AddMonths(-1);
        await PopulateMonthAsync(DisplayDate);
    }

    [RelayCommand]
    public async Task NextMonth()
    {
        DisplayDate = DisplayDate.AddMonths(1);
        await PopulateMonthAsync(DisplayDate);
    }

    [RelayCommand]
    public async Task GoToToday()
    {
        var today = TodayInDisplayZone();
        DisplayDate = new DateOnly(today.Year, today.Month, 1);
        await PopulateMonthAsync(DisplayDate);

        var todayCell = Days.FirstOrDefault(d => d.SolarDate == today);
        if (todayCell is not null)
        {
            SelectDay(todayCell);
        }
    }

    [RelayCommand]
    public void SelectDay(MonthDayViewModel day)
    {
        ArgumentNullException.ThrowIfNull(day);

        if (SelectedDay is not null)
        {
            SelectedDay.IsSelected = false;
        }

        SelectedDay = day;
        day.IsSelected = true;

        DaySelected?.Invoke(this, day.SolarDate);
    }

    /// <summary>
    /// Populates the 6×7 calendar grid with 42 day cells and loads events from store (NFR-PERF).
    /// Events are bucketed into days in the display time zone from <see cref="IDisplayPreferences"/> (FR-UI-7).
    /// </summary>
    public async Task PopulateMonthAsync(DateOnly date)
    {
        var version = ++_loadVersion;
        var stopwatch = Stopwatch.StartNew();

        var monthTitle = CalendarText.MonthYear(_localizationService, date);
        var leapMarker = CalendarText.LeapMarker(_localizationService);

        var firstDayOfMonth = new DateOnly(date.Year, date.Month, 1);
        int diff = ((int)firstDayOfMonth.DayOfWeek - (int)WeekStart + 7) % 7;
        var startGridDate = firstDayOfMonth.AddDays(-diff);
        var endGridDate = startGridDate.AddDays(42);

        var timeZone = _preferences.TimeZone;
        var today = TodayInDisplayZone();
        var cells = new List<MonthDayViewModel>(42);

        for (int i = 0; i < 42; i++)
        {
            var cellDate = startGridDate.AddDays(i);
            LunarDate? lunar = null;
            if (cellDate >= LunarCalendar.MinSolarDate && cellDate <= LunarCalendar.MaxSolarDate)
            {
                lunar = LunarCalendar.ToLunar(cellDate);
            }

            var cell = new MonthDayViewModel(
                cellDate,
                lunar,
                isCurrentMonth: cellDate.Month == date.Month && cellDate.Year == date.Year,
                isToday: cellDate == today,
                isLunarVisible: IsLunarVisible,
                leapMarker: leapMarker);

            if (SelectedDay is not null && cellDate == SelectedDay.SolarDate)
            {
                cell.IsSelected = true;
            }

            cells.Add(cell);
        }

        // Query events for the whole 6-week window, bounded by local midnights in the display time zone.
        var (startUtc, endUtc) = CalendarTime.GetUtcRange(startGridDate, endGridDate, timeZone);
        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = await _eventStore.GetEventsAsync(startUtc, endUtc);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            events = Array.Empty<CalendarEvent>();
        }

        if (version != _loadVersion)
        {
            return; // superseded by a newer request
        }

        MonthTitle = monthTitle;

        // Bucket events by every local date they cover (multi-day events appear on each day).
        var eventsByDate = new Dictionary<DateOnly, List<CalendarEvent>>();
        foreach (var ev in events.OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.StartUtc))
        {
            var (first, last) = CalendarTime.GetCoveredDates(ev, timeZone);
            if (first < startGridDate)
            {
                first = startGridDate;
            }

            for (var d = first; d <= last && d < endGridDate; d = d.AddDays(1))
            {
                if (!eventsByDate.TryGetValue(d, out var list))
                {
                    list = new List<CalendarEvent>();
                    eventsByDate[d] = list;
                }

                list.Add(ev);
            }
        }

        var moreEventsFormat = _localizationService.GetString("month.more_events", _localizationService.CurrentCulture);

        foreach (var cell in cells)
        {
            if (eventsByDate.TryGetValue(cell.SolarDate, out var dayEvents))
            {
                cell.SetEvents(dayEvents, moreEventsFormat);
            }
        }

        stopwatch.Stop();
        RenderDuration = stopwatch.Elapsed;

        Days.Clear();
        foreach (var cell in cells)
        {
            Days.Add(cell);
        }
    }

    private DateOnly TodayInDisplayZone() => CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, _preferences.TimeZone);

    private void RebuildWeekdayHeaders()
    {
        WeekdayHeaders.Clear();

        var day = WeekStart;
        for (int i = 0; i < 7; i++)
        {
            WeekdayHeaders.Add(CalendarText.WeekdayShort(_localizationService, day));
            day = (DayOfWeek)(((int)day + 1) % 7);
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        RebuildWeekdayHeaders();
        _ = PopulateMonthAsync(DisplayDate);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
    }
}
