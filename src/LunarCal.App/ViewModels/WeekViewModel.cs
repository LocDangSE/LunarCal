using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the 7-day week calendar view (Figma S06, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class WeekViewModel : ObservableObject, IDisposable
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
    /// Vertical offset the time grid opens at: 07:00, as in Figma S06/S07 (the grid still covers 00:00–24:00).
    /// </summary>
    public double InitialScrollOffset { get; } = DayColumnViewModel.HourRowHeight * DayColumnViewModel.FirstVisibleHour;

    /// <summary>Time zone label in the hour gutter (Figma S06 24:5): "GMT+7".</summary>
    public string TimeZoneLabel => CalendarText.GmtOffset(_localizationService, _preferences.TimeZone, DateTimeOffset.UtcNow);

    /// <summary>
    /// Initializes a new instance of <see cref="WeekViewModel"/>.
    /// </summary>
    public WeekViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        IDisplayPreferences preferences)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        _displayDate = CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, _preferences.TimeZone);
        _isLunarVisible = _preferences.LunarEnabled;
        _weekStart = _preferences.WeekStart;

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
        IsLunarVisible = _preferences.LunarEnabled;
        WeekStart = _preferences.WeekStart;
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
        var version = ++_loadVersion;
        var sw = Stopwatch.StartNew();

        int diff = (7 + (targetDate.DayOfWeek - WeekStart)) % 7;
        WeekStartDate = targetDate.AddDays(-diff);
        WeekEndDate = WeekStartDate.AddDays(6);

        var timeZone = _preferences.TimeZone;
        var today = CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, timeZone);
        var culture = _localizationService.CurrentCulture;

        var leapMarker = CalendarText.LeapMarker(_localizationService);

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

            var colVm = new DayColumnViewModel(colDate, lunar, dayName, isToday, IsLunarVisible, leapMarker, _localizationService);
            columns.Add(colVm);
        }

        // Query events from store
        var (startUtc, endUtc) = CalendarTime.GetUtcRange(WeekStartDate, WeekEndDate.AddDays(1), timeZone);

        IReadOnlyList<CalendarEvent> events;
        IReadOnlyDictionary<string, CalendarInfo> colors;
        try
        {
            events = await _eventStore.GetEventsAsync(startUtc, endUtc, cancellationToken: cancellationToken);
            colors = await CalendarColors.LoadAsync(_eventStore, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            events = Array.Empty<CalendarEvent>();
            colors = new Dictionary<string, CalendarInfo>();
        }

        if (version != _loadVersion)
        {
            return; // superseded by a newer request
        }

        // Distribute events to columns
        foreach (var col in columns)
        {
            col.PopulateEvents(events, timeZone, _preferences.TimeFormat, culture, colors);
        }

        DayColumns.Clear();
        foreach (var col in columns)
        {
            DayColumns.Add(col);
        }

        WeekTitle = CalendarText.WeekRange(_localizationService, WeekStartDate, WeekEndDate);

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
