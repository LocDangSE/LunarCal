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
    private readonly IMutationService? _mutationService;

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
        IDisplayPreferences preferences,
        IMutationService? mutationService = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _mutationService = mutationService;

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

    /// <summary>Raised when the user asks to open the event editor dialog (Figma S10).</summary>
    public event EventHandler<EventEditorRequest>? EventEditorRequested;

    /// <summary>
    /// Adjusts an event's start and end times by a time delta (drag and drop time adjustment, FR-UI-4).
    /// </summary>
    public async Task<CalendarEvent?> AdjustEventTimeAsync(
        string eventId,
        TimeSpan timeDelta,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (_mutationService is null)
        {
            return null;
        }

        var ev = await _eventStore.GetEventByIdAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (ev is null)
        {
            return null;
        }

        var updated = ev with
        {
            StartUtc = ev.StartUtc + timeDelta,
            EndUtc = ev.EndUtc + timeDelta
        };

        var accountId = "primary";
        var result = await _mutationService.UpdateEventOptimisticAsync(accountId, updated.CalendarId, updated, cancellationToken).ConfigureAwait(false);
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Sets an event's explicit start and end times (FR-UI-4).
    /// </summary>
    public async Task<CalendarEvent?> SetEventTimeAsync(
        string eventId,
        DateTimeOffset newStartUtc,
        DateTimeOffset newEndUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (_mutationService is null)
        {
            return null;
        }

        var ev = await _eventStore.GetEventByIdAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (ev is null)
        {
            return null;
        }

        var updated = ev with
        {
            StartUtc = newStartUtc,
            EndUtc = newEndUtc
        };

        var accountId = "primary";
        var result = await _mutationService.UpdateEventOptimisticAsync(accountId, updated.CalendarId, updated, cancellationToken).ConfigureAwait(false);
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
        return result;
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
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateWeekAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
        return result;
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
