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
/// View model controlling the single day calendar view (Figma S07, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class DayViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsStore _settingsStore;

    [ObservableProperty]
    private DateOnly _displayDate;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private DayColumnViewModel? _dayColumn;

    [ObservableProperty]
    private string _dayTitle = string.Empty;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    /// <summary>The 24 hour labels (00:00 .. 23:00) displayed in the left hour column.</summary>
    public ObservableCollection<string> HourLabels { get; } = new();

    /// <summary>Detail panel child view model (Figma S07 &amp; S09, FR-UI-3).</summary>
    public DayDetailViewModel DetailPanel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="DayViewModel"/>.
    /// </summary>
    public DayViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _displayDate = DateOnly.FromDateTime(DateTime.Today);
        DetailPanel = new DayDetailViewModel(_eventStore, _localizationService, _settingsStore);

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
        await PopulateDayAsync(DisplayDate);
    }

    /// <summary>
    /// Sets whether lunar dates are displayed (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        DayColumn?.SetLunarVisibility(visible);
        DetailPanel.SetLunarVisibility(visible);
    }

    /// <summary>
    /// Sets the culture and updates day name and titles.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        DetailPanel.SetCulture(_localizationService.CurrentCulture);
        _ = PopulateDayAsync(DisplayDate);
    }

    /// <summary>
    /// Sets the focal date and recalculates the day view and events.
    /// </summary>
    public async Task SetDisplayDateAsync(DateOnly date)
    {
        DisplayDate = date;
        await PopulateDayAsync(date);
    }

    /// <summary>
    /// Populates the day column for <paramref name="targetDate"/>,
    /// querying events and measuring render performance (FR-UI-1).
    /// </summary>
    public async Task PopulateDayAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var culture = _localizationService.CurrentCulture;

        LunarDate? lunar = null;
        if (targetDate >= LunarCalendar.MinSolarDate && targetDate <= LunarCalendar.MaxSolarDate)
        {
            lunar = LunarCalendar.ToLunar(targetDate);
        }

        var dayName = culture.DateTimeFormat.GetDayName(targetDate.DayOfWeek);
        var isToday = targetDate == today;

        var col = new DayColumnViewModel(targetDate, lunar, dayName, isToday, IsLunarVisible);

        // Query events from store
        var offset = TimeSpan.FromHours(7);
        var startUtc = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var endUtc = new DateTimeOffset(targetDate.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();

        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = await _eventStore.GetEventsAsync(startUtc, endUtc, cancellationToken: cancellationToken);
        }
        catch
        {
            events = Array.Empty<CalendarEvent>();
        }

        col.PopulateEvents(events, timezoneOffset: offset);
        DayColumn = col;

        // Day title
        DayTitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
            ? $"{dayName}, {targetDate.Day} Tháng {targetDate.Month}, {targetDate.Year}"
            : targetDate.ToString("dddd, MMMM d, yyyy", culture);

        await DetailPanel.SetDateAsync(targetDate, cancellationToken);

        sw.Stop();
        RenderDuration = sw.Elapsed;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DetailPanel.SetCulture(_localizationService.CurrentCulture);
        _ = PopulateDayAsync(DisplayDate);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        DetailPanel.Dispose();
    }
}
