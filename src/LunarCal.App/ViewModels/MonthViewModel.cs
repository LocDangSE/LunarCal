using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the 6×7 month grid calendar view (FR-UI-1, FR-UI-2, FR-LUNAR-6, NFR-PERF).
/// </summary>
public sealed partial class MonthViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsStore _settingsStore;

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
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        var today = DateOnly.FromDateTime(DateTime.Today);
        _displayDate = new DateOnly(today.Year, today.Month, 1);

        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAsync();
    }

    /// <summary>
    /// Loads settings and initializes week start and lunar visibility (FR-UI-7, FR-LUNAR-6).
    /// </summary>
    public async Task InitializeSettingsAsync()
    {
        IsLunarVisible = await _settingsStore.GetBoolAsync(SettingKeys.LunarEnabled, defaultValue: true);
        var weekStartStr = await _settingsStore.GetStringAsync(SettingKeys.WeekStart);
        if (Enum.TryParse<DayOfWeek>(weekStartStr, ignoreCase: true, out var ws))
        {
            WeekStart = ws;
        }

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
        var today = DateOnly.FromDateTime(DateTime.Today);
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
    /// </summary>
    public async Task PopulateMonthAsync(DateOnly date)
    {
        var stopwatch = Stopwatch.StartNew();

        var culture = _localizationService.CurrentCulture;
        MonthTitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
            ? $"Tháng {date.Month}, {date.Year}"
            : date.ToString("MMMM yyyy", culture);

        var firstDayOfMonth = new DateOnly(date.Year, date.Month, 1);
        int diff = ((int)firstDayOfMonth.DayOfWeek - (int)WeekStart + 7) % 7;
        var startGridDate = firstDayOfMonth.AddDays(-diff);
        var endGridDate = startGridDate.AddDays(42);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var cells = new List<MonthDayViewModel>(42);

        for (int i = 0; i < 42; i++)
        {
            var cellDate = startGridDate.AddDays(i);
            LunarDate? lunar = null;
            try
            {
                lunar = LunarCalendar.ToLunar(cellDate);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Date outside supported 1900-2100 range
            }

            var cell = new MonthDayViewModel(
                cellDate,
                lunar,
                isCurrentMonth: cellDate.Month == date.Month && cellDate.Year == date.Year,
                isToday: cellDate == today,
                isLunarVisible: IsLunarVisible);

            if (SelectedDay is not null && cellDate == SelectedDay.SolarDate)
            {
                cell.IsSelected = true;
            }

            cells.Add(cell);
        }

        // Query events from store for the entire 6-week window
        var startUtc = new DateTimeOffset(startGridDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(endGridDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var events = await _eventStore.GetEventsAsync(startUtc, endUtc);

        // Group events by local date
        var eventsByDate = events
            .GroupBy(e => DateOnly.FromDateTime(e.StartUtc.LocalDateTime))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEvent>)g.ToList());

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

    private void RebuildWeekdayHeaders(CultureInfo? culture = null)
    {
        var targetCulture = culture ?? _localizationService.CurrentCulture;
        WeekdayHeaders.Clear();

        var day = WeekStart;
        for (int i = 0; i < 7; i++)
        {
            var header = targetCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                ? day switch
                {
                    DayOfWeek.Monday => "Th 2",
                    DayOfWeek.Tuesday => "Th 3",
                    DayOfWeek.Wednesday => "Th 4",
                    DayOfWeek.Thursday => "Th 5",
                    DayOfWeek.Friday => "Th 6",
                    DayOfWeek.Saturday => "Th 7",
                    DayOfWeek.Sunday => "CN",
                    _ => string.Empty,
                }
                : targetCulture.DateTimeFormat.GetAbbreviatedDayName(day);

            WeekdayHeaders.Add(header);
            day = (DayOfWeek)(((int)day + 1) % 7);
        }
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RebuildWeekdayHeaders();
        _ = PopulateMonthAsync(DisplayDate);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
    }
}
