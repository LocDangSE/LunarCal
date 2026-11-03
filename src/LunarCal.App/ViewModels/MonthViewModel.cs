using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the 6×7 month grid view (Figma 01, FR-UI-1, FR-UI-2, FR-LUNAR-6).
/// </summary>
public sealed partial class MonthViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly IDisplayPreferences _preferences;
    private readonly IMutationService? _mutationService;

    // Incremented by every load request; a load whose version is no longer current drops its result, so the
    // latest request always wins even when earlier store calls complete later.
    private int _loadVersion;

    [ObservableProperty]
    private DateOnly _displayDate;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private DayOfWeek _weekStart = DayOfWeek.Monday;

    [ObservableProperty]
    private MonthDayViewModel? _selectedDay;

    [ObservableProperty]
    private string _monthTitle = string.Empty;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    private DateOnly? _selectedDate;

    /// <summary>Raised when a day cell is selected.</summary>
    public event EventHandler<DateOnly>? DaySelected;

    /// <summary>Raised when the user asks to open the event editor dialog (Figma S10).</summary>
    public event EventHandler<EventEditorRequest>? EventEditorRequested;

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
        IDisplayPreferences preferences,
        IMutationService? mutationService = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _mutationService = mutationService;

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
        _selectedDate = day.SolarDate;
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
        var lunarDates = new LunarDate?[42];
        for (int i = 0; i < 42; i++)
        {
            var cellDate = startGridDate.AddDays(i);
            if (cellDate >= LunarCalendar.MinSolarDate && cellDate <= LunarCalendar.MaxSolarDate)
            {
                lunarDates[i] = LunarCalendar.ToLunar(cellDate);
            }
        }

        // Query events for the whole 6-week window, bounded by local midnights in the display time zone.
        var (startUtc, endUtc) = CalendarTime.GetUtcRange(startGridDate, endGridDate, timeZone);
        IReadOnlyList<CalendarEvent> events;
        IReadOnlyList<CalendarInfo>? calendars;
        try
        {
            events = await _eventStore.GetEventsAsync(startUtc, endUtc);
            calendars = await _eventStore.GetCalendarsAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            events = Array.Empty<CalendarEvent>();
            calendars = null;
        }

        if (version != _loadVersion)
        {
            return; // superseded by a newer request
        }

        var colorMap = (calendars ?? Array.Empty<CalendarInfo>())
            .Where(c => !string.IsNullOrEmpty(c.Color))
            .ToDictionary(c => c.Id, c => c.Color!, StringComparer.Ordinal);

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

        var moreEventsFormat = _localizationService["month.more_events"];
        var culture = _localizationService.CurrentCulture;
        var timeFormat = _preferences.TimeFormat;
        string LabelOf(CalendarEvent ev) => ev.AllDay
            ? ev.Title
            : string.Create(culture, $"{TimeFormattingHelper.FormatTime(CalendarTime.ToLocal(ev.StartUtc, timeZone), timeFormat, culture)} {ev.Title}");
        string? ColorOf(CalendarEvent ev) => colorMap.TryGetValue(ev.CalendarId, out var c) ? c : null;

        // Reuse the existing 42 cells when possible: WPF then only refreshes bindings instead of regenerating
        // every cell container, which dominates the month switch cost (NFR-PERF).
        var reuse = Days.Count == 42;
        var cells = reuse ? Days.ToList() : new List<MonthDayViewModel>(42);
        MonthDayViewModel? selected = null;
        for (int i = 0; i < 42; i++)
        {
            var cellDate = startGridDate.AddDays(i);
            var isCurrentMonth = cellDate.Month == date.Month && cellDate.Year == date.Year;
            MonthDayViewModel cell;
            if (reuse)
            {
                cell = cells[i];
                cell.Reset(cellDate, lunarDates[i], isCurrentMonth, cellDate == today, IsLunarVisible, leapMarker);
            }
            else
            {
                cell = new MonthDayViewModel(cellDate, lunarDates[i], isCurrentMonth, cellDate == today, IsLunarVisible, leapMarker);
                cells.Add(cell);
            }

            if (cellDate == _selectedDate)
            {
                cell.IsSelected = true;
                selected = cell;
            }

            cell.SetEvents(
                eventsByDate.TryGetValue(cellDate, out var dayEvents) ? dayEvents : Array.Empty<CalendarEvent>(),
                moreEventsFormat,
                LabelOf,
                ColorOf);
        }

        MonthTitle = monthTitle;
        SelectedDay = selected;

        if (!reuse)
        {
            Days.Clear();
            foreach (var cell in cells)
            {
                Days.Add(cell);
            }
        }

        stopwatch.Stop();
        RenderDuration = stopwatch.Elapsed;
    }

    /// <summary>Opens the editor dialog for an existing event (Figma S10).</summary>
    public void OpenEvent(CalendarEvent calendarEvent)
    {
        EventEditorRequested?.Invoke(this, new EventEditorRequest(EventEditorMode.Edit, Event: calendarEvent));
    }

    /// <summary>Opens the editor dialog for a new event at the specified date and time.</summary>
    public void CreateEventAt(DateOnly date, TimeOnly time)
    {
        EventEditorRequested?.Invoke(this, new EventEditorRequest(EventEditorMode.Create, Date: date, Time: time));
    }

    /// <summary>Opens the editor dialog for an all-day event.</summary>
    public void CreateEventAllDay(DateOnly date)
    {
        EventEditorRequested?.Invoke(this, new EventEditorRequest(EventEditorMode.Create, Date: date, IsAllDay: true));
    }

    /// <summary>Opens the editor dialog for creating a lunar rule.</summary>
    public void CreateLunarRule(DateOnly date)
    {
        EventEditorRequested?.Invoke(this, new EventEditorRequest(EventEditorMode.CreateLunarRule, Date: date, IsAllDay: true));
    }

    /// <summary>Duplicates an event (Figma S10 context menu).</summary>
    public async Task<CalendarEvent?> DuplicateEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (_mutationService is null)
        {
            return null;
        }

        var copy = calendarEvent with
        {
            Id = Guid.NewGuid().ToString("D"),
            GoogleId = null,
            Etag = null,
            StartUtc = calendarEvent.AllDay ? calendarEvent.StartUtc.AddDays(1) : calendarEvent.StartUtc.AddHours(1),
            EndUtc = calendarEvent.AllDay ? calendarEvent.EndUtc.AddDays(1) : calendarEvent.EndUtc.AddHours(1)
        };

        var accountId = "primary";
        var result = await _mutationService.CreateEventOptimisticAsync(accountId, copy.CalendarId, copy, cancellationToken).ConfigureAwait(false);
        await PopulateMonthAsync(DisplayDate).ConfigureAwait(false);
        return result;
    }

    /// <summary>Moves an event to another calendar (Figma S10 context menu).</summary>
    public async Task MoveEventToCalendarAsync(CalendarEvent calendarEvent, string targetCalendarId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCalendarId);
        if (_mutationService is null)
        {
            return;
        }

        var updated = calendarEvent with { CalendarId = targetCalendarId };
        var accountId = "primary";
        await _mutationService.UpdateEventOptimisticAsync(accountId, targetCalendarId, updated, cancellationToken).ConfigureAwait(false);
        await PopulateMonthAsync(DisplayDate).ConfigureAwait(false);
    }

    /// <summary>Deletes an event (Figma S10 context menu).</summary>
    public async Task DeleteEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (_mutationService is null)
        {
            return;
        }

        var accountId = "primary";
        await _mutationService.DeleteEventOptimisticAsync(accountId, calendarEvent.CalendarId, calendarEvent.Id, calendarEvent.Etag, cancellationToken).ConfigureAwait(false);
        await PopulateMonthAsync(DisplayDate).ConfigureAwait(false);
    }

    /// <summary>Pastes the copied event from clipboard at the specified date and time.</summary>
    public async Task<CalendarEvent?> PasteEventAsync(DateOnly date, TimeOnly? time = null, CancellationToken cancellationToken = default)
    {
        if (!EventClipboard.HasEvent || EventClipboard.Current is null || _mutationService is null)
        {
            return null;
        }

        var source = EventClipboard.Current;
        var timeZone = _preferences.TimeZone;
        DateTimeOffset startUtc;
        DateTimeOffset endUtc;

        if (source.AllDay)
        {
            startUtc = CalendarTime.StartOfDayUtc(date, timeZone);
            endUtc = CalendarTime.StartOfDayUtc(date.AddDays(1), timeZone);
        }
        else
        {
            var targetTime = time ?? new TimeOnly(9, 0);
            var duration = source.EndUtc - source.StartUtc;
            startUtc = CalendarTime.ToUtc(date, targetTime, timeZone);
            endUtc = startUtc + (duration > TimeSpan.Zero ? duration : TimeSpan.FromHours(1));
        }

        var pasted = source with
        {
            Id = Guid.NewGuid().ToString("D"),
            GoogleId = null,
            Etag = null,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = source.AllDay
        };

        var accountId = "primary";
        var result = await _mutationService.CreateEventOptimisticAsync(accountId, pasted.CalendarId, pasted, cancellationToken).ConfigureAwait(false);
        await PopulateMonthAsync(DisplayDate).ConfigureAwait(false);
        return result;
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
