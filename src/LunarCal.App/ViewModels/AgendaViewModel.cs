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
/// View model controlling the Agenda (Schedule) calendar view (Figma S08, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class AgendaViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsStore _settingsStore;

    [ObservableProperty]
    private DateOnly _displayDate;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private string _periodTitle = string.Empty;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private bool _isAllCalendarsSelected = true;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    /// <summary>Filter chips for toggling individual calendars on or off (Figma S08).</summary>
    public ObservableCollection<CalendarFilterChipViewModel> CalendarFilters { get; } = new();

    /// <summary>Event groups categorized by day.</summary>
    public ObservableCollection<AgendaDayGroupViewModel> DayGroups { get; } = new();

    private IReadOnlyList<CalendarEvent> _cachedEvents = Array.Empty<CalendarEvent>();
    private IReadOnlyList<CalendarInfo> _cachedCalendars = Array.Empty<CalendarInfo>();

    /// <summary>
    /// Initializes a new instance of <see cref="AgendaViewModel"/>.
    /// </summary>
    public AgendaViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _displayDate = DateOnly.FromDateTime(DateTime.Today);
        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAndLoadAsync();
    }

    private async Task InitializeSettingsAndLoadAsync()
    {
        IsLunarVisible = await _settingsStore.GetBoolAsync(SettingKeys.LunarEnabled, defaultValue: true);
        await LoadDataAsync();
    }

    /// <summary>
    /// Sets whether lunar dates are displayed in day group headers (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        foreach (var group in DayGroups)
        {
            group.SetLunarVisibility(visible);
        }
    }

    /// <summary>
    /// Updates the culture and refreshes formatted strings.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// Sets the focal date and reloads agenda events for that period.
    /// </summary>
    public async Task SetDisplayDateAsync(DateOnly date)
    {
        DisplayDate = date;
        await LoadDataAsync();
    }

    /// <summary>
    /// Toggles all calendar filter chips on or off.
    /// </summary>
    [RelayCommand]
    public void ToggleAllCalendars()
    {
        bool targetState = !IsAllCalendarsSelected;
        foreach (var chip in CalendarFilters)
        {
            chip.IsSelected = targetState;
        }

        IsAllCalendarsSelected = targetState;
        RebuildDayGroups();
    }

    /// <summary>
    /// Loads calendar definitions and event records from the store, then populates groups (FR-UI-1).
    /// </summary>
    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var monthStart = new DateOnly(DisplayDate.Year, DisplayDate.Month, 1);
        var monthEnd = new DateOnly(DisplayDate.Year, DisplayDate.Month, DateTime.DaysInMonth(DisplayDate.Year, DisplayDate.Month));

        var offset = TimeSpan.FromHours(7);
        var startUtc = new DateTimeOffset(monthStart.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var endUtc = new DateTimeOffset(monthEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();

        try
        {
            _cachedCalendars = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken);
            _cachedEvents = await _eventStore.GetEventsAsync(startUtc, endUtc, cancellationToken: cancellationToken);
        }
        catch
        {
            _cachedCalendars = Array.Empty<CalendarInfo>();
            _cachedEvents = Array.Empty<CalendarEvent>();
        }

        // Sync calendar filter chips
        SyncCalendarFilters();

        // Rebuild day groups based on active filters
        RebuildDayGroups();

        // Update period title
        var culture = _localizationService.CurrentCulture;
        PeriodTitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
            ? $"Tháng {DisplayDate.Month}, {DisplayDate.Year}"
            : DisplayDate.ToString("MMMM yyyy", culture);

        sw.Stop();
        RenderDuration = sw.Elapsed;
    }

    private void SyncCalendarFilters()
    {
        var currentSelection = CalendarFilters.ToDictionary(c => c.Id, c => c.IsSelected, StringComparer.Ordinal);

        foreach (var existing in CalendarFilters)
        {
            existing.SelectionChanged -= OnFilterChipSelectionChanged;
        }

        CalendarFilters.Clear();
        foreach (var cal in _cachedCalendars)
        {
            bool isSelected = !currentSelection.TryGetValue(cal.Id, out bool sel) || sel;
            var chip = new CalendarFilterChipViewModel(cal.Id, cal.Summary, cal.Color ?? "#2F6BFF", isSelected);
            chip.SelectionChanged += OnFilterChipSelectionChanged;
            CalendarFilters.Add(chip);
        }

        IsAllCalendarsSelected = CalendarFilters.Count == 0 || CalendarFilters.All(c => c.IsSelected);
    }

    private void OnFilterChipSelectionChanged(object? sender, EventArgs e)
    {
        IsAllCalendarsSelected = CalendarFilters.Count > 0 && CalendarFilters.All(c => c.IsSelected);
        RebuildDayGroups();
    }

    /// <summary>
    /// Groups events by date and filters by active calendar selections (FR-UI-1).
    /// </summary>
    public void RebuildDayGroups()
    {
        var activeCalendarIds = CalendarFilters
            .Where(c => c.IsSelected)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        var calendarLookup = _cachedCalendars.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        var offset = TimeSpan.FromHours(7);
        var culture = _localizationService.CurrentCulture;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var allDayLabel = _localizationService["calendar.all_day"];

        var filteredEvents = _cachedEvents
            .Where(e => activeCalendarIds.Contains(e.CalendarId))
            .ToList();

        // Group filtered events by date
        var eventsByDate = new SortedDictionary<DateOnly, List<CalendarEvent>>();

        foreach (var ev in filteredEvents)
        {
            var eventStartDate = DateOnly.FromDateTime(ev.StartUtc.ToOffset(offset).DateTime);
            var eventEndDate = DateOnly.FromDateTime(ev.EndUtc.ToOffset(offset).DateTime);

            for (var d = eventStartDate; d <= eventEndDate; d = d.AddDays(1))
            {
                if (!eventsByDate.TryGetValue(d, out var list))
                {
                    list = new List<CalendarEvent>();
                    eventsByDate[d] = list;
                }

                list.Add(ev);
            }
        }

        DayGroups.Clear();

        foreach (var (date, events) in eventsByDate)
        {
            LunarDate? lunar = null;
            if (date >= LunarCalendar.MinSolarDate && date <= LunarCalendar.MaxSolarDate)
            {
                lunar = LunarCalendar.ToLunar(date);
            }

            var dayName = culture.DateTimeFormat.GetDayName(date.DayOfWeek);
            var isToday = date == today;

            string dateDisplay;
            if (culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
            {
                dateDisplay = isToday
                    ? $"Hôm nay, {date.Day} Tháng {date.Month}"
                    : $"{dayName}, {date.Day} Tháng {date.Month}";
            }
            else
            {
                dateDisplay = isToday
                    ? $"Today, {date:MMM d}"
                    : date.ToString("dddd, MMM d", culture);
            }

            var groupVm = new AgendaDayGroupViewModel(date, lunar, dayName, dateDisplay, isToday, IsLunarVisible);

            // Sort events: all-day first, then by start time
            var sortedEvents = events
                .OrderBy(e => e.AllDay ? 0 : 1)
                .ThenBy(e => e.StartUtc)
                .ToList();

            foreach (var ev in sortedEvents)
            {
                var calInfo = calendarLookup.TryGetValue(ev.CalendarId, out var cal) ? cal : null;
                var color = calInfo?.Color ?? "#2F6BFF";
                var calName = calInfo?.Summary ?? string.Empty;

                string timeDisplay;
                if (ev.AllDay)
                {
                    timeDisplay = allDayLabel;
                }
                else
                {
                    timeDisplay = $"{ev.StartUtc.ToOffset(offset):HH:mm} – {ev.EndUtc.ToOffset(offset):HH:mm}";
                }

                groupVm.Events.Add(new AgendaEventItemViewModel(ev, color, calName, timeDisplay));
            }

            DayGroups.Add(groupVm);
        }

        HasEvents = DayGroups.Count > 0;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = LoadDataAsync();
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        foreach (var chip in CalendarFilters)
        {
            chip.SelectionChanged -= OnFilterChipSelectionChanged;
        }
    }
}
