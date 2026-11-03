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
/// View model controlling the single day calendar view (Figma S07, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class DayViewModel : ObservableObject, IDisposable
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
    private DayColumnViewModel? _dayColumn;

    [ObservableProperty]
    private string _dayTitle = string.Empty;

    [ObservableProperty]
    private TimeSpan _renderDuration;

    /// <summary>The 24 hour labels (00:00 .. 23:00) displayed in the left hour column.</summary>
    public ObservableCollection<string> HourLabels { get; } = new();

    /// <summary>
    /// Vertical offset the time grid opens at: 07:00, as in Figma S06/S07 (the grid still covers 00:00–24:00).
    /// </summary>
    public double InitialScrollOffset { get; } = DayColumnViewModel.HourRowHeight * DayColumnViewModel.FirstVisibleHour;

    /// <summary>Time zone label in the hour gutter (Figma S06 24:5): "GMT+7".</summary>
    public string TimeZoneLabel => CalendarText.GmtOffset(_localizationService, _preferences.TimeZone, DateTimeOffset.UtcNow);

    /// <summary>Detail panel child view model (Figma S07 &amp; S09, FR-UI-3).</summary>
    public DayDetailViewModel DetailPanel { get; }

    /// <summary>Raised when the user asks to open the event editor dialog (Figma S10).</summary>
    public event EventHandler<EventEditorRequest>? EventEditorRequested;

    /// <summary>
    /// Initializes a new instance of <see cref="DayViewModel"/>.
    /// </summary>
    public DayViewModel(
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
        DetailPanel = new DayDetailViewModel(_eventStore, _localizationService, _preferences);
        DetailPanel.AddEventRequested += OnDetailAddEventRequested;
        DetailPanel.AddLunarRuleRequested += OnDetailAddLunarRuleRequested;
        DetailPanel.EventSelected += OnDetailEventSelected;

        InitializeHourLabels();
        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAndLoadAsync();
    }

    private void OnDetailAddEventRequested(object? sender, DateOnly date) => CreateEventAllDay(date);

    private void OnDetailAddLunarRuleRequested(object? sender, DateOnly date) => CreateLunarRule(date);

    private void OnDetailEventSelected(object? sender, CalendarEvent ev) => OpenEvent(ev);

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
        var version = ++_loadVersion;
        var sw = Stopwatch.StartNew();

        var timeZone = _preferences.TimeZone;
        var today = CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, timeZone);
        var culture = _localizationService.CurrentCulture;

        LunarDate? lunar = null;
        if (targetDate >= LunarCalendar.MinSolarDate && targetDate <= LunarCalendar.MaxSolarDate)
        {
            lunar = LunarCalendar.ToLunar(targetDate);
        }

        var dayName = culture.DateTimeFormat.GetDayName(targetDate.DayOfWeek);
        var isToday = targetDate == today;

        var col = new DayColumnViewModel(
            targetDate, lunar, dayName, isToday, IsLunarVisible, CalendarText.LeapMarker(_localizationService), _localizationService);

        // Query events from store
        var (startUtc, endUtc) = CalendarTime.GetUtcRange(targetDate, targetDate.AddDays(1), timeZone);

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

        col.PopulateEvents(events, timeZone, _preferences.TimeFormat, culture, colors);
        DayColumn = col;

        // Day title
        DayTitle = CalendarText.DayFull(_localizationService, targetDate);

        await DetailPanel.SetDateAsync(targetDate, cancellationToken);

        sw.Stop();
        RenderDuration = sw.Elapsed;
    }

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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
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
        await PopulateDayAsync(DisplayDate, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DetailPanel.SetCulture(_localizationService.CurrentCulture);
        _ = PopulateDayAsync(DisplayDate);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        DetailPanel.AddEventRequested -= OnDetailAddEventRequested;
        DetailPanel.AddLunarRuleRequested -= OnDetailAddLunarRuleRequested;
        DetailPanel.EventSelected -= OnDetailEventSelected;
        DetailPanel.Dispose();
    }
}
