using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the 7-day week calendar view (Figma S06, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class WeekViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsStore _settingsStore;

    [ObservableProperty]
    private DateOnly _displayDate;

    [ObservableProperty]
    private DateOnly _weekStartDate;

    [ObservableProperty]
    private DateOnly _weekEndDate;

    [ObservableProperty]
    private DayOfWeek _weekStart = DayOfWeek.Monday;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private string _weekTitle = string.Empty;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    /// <summary>The 7 day columns in this week view.</summary>
    public ObservableCollection<DayColumnViewModel> DayColumns { get; } = new();

    /// <summary>The 24 hour labels (00:00 .. 23:00) displayed in the left hour column.</summary>
    public ObservableCollection<string> HourLabels { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="WeekViewModel"/>.
    /// </summary>
    public WeekViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _displayDate = DateOnly.FromDateTime(DateTime.Today);

        InitializeHourLabels();
        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAndLoadAsync();
    }

    private void InitializeHourLabels()
    {
        HourLabels.Clear();
        for (int h = 0; h < 24; h++)
        {
            HourLabels.Add($"{h:D2}:00");
        }
    }

    private async Task InitializeSettingsAndLoadAsync()
    {
        IsLunarVisible = await _settingsStore.GetBoolAsync(SettingKeys.LunarEnabled, defaultValue: true);
        var weekStartStr = await _settingsStore.GetStringAsync(SettingKeys.WeekStart);
        if (Enum.TryParse<DayOfWeek>(weekStartStr, ignoreCase: true, out var ws))
        {
            WeekStart = ws;
        }

        await PopulateWeekAsync(DisplayDate);
    }

    /// <summary>
    /// Sets the week start day and rebuilds the week view.
    /// </summary>
    public void SetWeekStart(DayOfWeek weekStart)
    {
        WeekStart = weekStart;
        _ = PopulateWeekAsync(DisplayDate);
    }

    /// <summary>
    /// Sets culture and week start day, updating headers.
    /// </summary>
    public void SetCultureAndWeekStart(CultureInfo culture, DayOfWeek weekStart)
    {
        WeekStart = weekStart;
        _ = PopulateWeekAsync(DisplayDate);
    }

    /// <summary>
    /// Sets whether lunar dates are displayed across all week columns (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        foreach (var col in DayColumns)
        {
            col.SetLunarVisibility(visible);
        }
    }

    /// <summary>
    /// Sets the focal date and recalculates the week columns and events.
    /// </summary>
    public async Task SetDisplayDateAsync(DateOnly date)
    {
        DisplayDate = date;
        await PopulateWeekAsync(date);
    }

    /// <summary>
    /// Populates the 7 day columns for the week containing <paramref name="targetDate"/>,
    /// querying events and measuring render performance (FR-UI-1).
    /// </summary>
    public async Task PopulateWeekAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        int diff = (7 + (targetDate.DayOfWeek - WeekStart)) % 7;
        WeekStartDate = targetDate.AddDays(-diff);
        WeekEndDate = WeekStartDate.AddDays(6);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var culture = _localizationService.CurrentCulture;

        // Build 7 column view models
        var columns = new List<DayColumnViewModel>(7);
        for (int i = 0; i < 7; i++)
        {
            var colDate = WeekStartDate.AddDays(i);
            LunarDate? lunar = null;
            if (colDate >= LunarCalendar.MinSolarDate && colDate <= LunarCalendar.MaxSolarDate)
            {
                lunar = LunarCalendar.ToLunar(colDate);
            }

            var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(colDate.DayOfWeek);
            var isToday = colDate == today;

            var colVm = new DayColumnViewModel(colDate, lunar, dayName, isToday, IsLunarVisible);
            columns.Add(colVm);
        }

        // Query events from store
        var offset = TimeSpan.FromHours(7);
        var startUtc = new DateTimeOffset(WeekStartDate.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var endUtc = new DateTimeOffset(WeekEndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();

        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = await _eventStore.GetEventsAsync(startUtc, endUtc, cancellationToken: cancellationToken);
        }
        catch
        {
            events = Array.Empty<CalendarEvent>();
        }

        // Distribute events to columns
        foreach (var col in columns)
        {
            var colEvents = events.Where(e =>
            {
                var evStart = e.StartUtc.ToOffset(offset).Date;
                var evEnd = e.EndUtc.ToOffset(offset).Date;
                var target = col.Date.ToDateTime(TimeOnly.MinValue);
                return evStart <= target && evEnd >= target;
            }).ToList();

            col.PopulateEvents(colEvents, timezoneOffset: offset);
        }

        DayColumns.Clear();
        foreach (var col in columns)
        {
            DayColumns.Add(col);
        }

        // Update week title
        if (culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            WeekTitle = WeekStartDate.Month == WeekEndDate.Month
                ? $"{WeekStartDate.Day} – {WeekEndDate.Day} Tháng {WeekStartDate.Month}, {WeekStartDate.Year}"
                : $"{WeekStartDate.Day}/{WeekStartDate.Month} – {WeekEndDate.Day}/{WeekEndDate.Month}, {WeekStartDate.Year}";
        }
        else
        {
            WeekTitle = WeekStartDate.Month == WeekEndDate.Month
                ? $"{WeekStartDate:MMM d} – {WeekEndDate.Day}, {WeekStartDate.Year}"
                : $"{WeekStartDate:MMM d} – {WeekEndDate:MMM d, yyyy}";
        }

        sw.Stop();
        RenderDuration = sw.Elapsed;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = PopulateWeekAsync(DisplayDate);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
    }
}
