using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Display item representing an event recurrence option.
/// </summary>
public sealed record RecurrenceOption(EventRecurrenceType Type, string Label);

/// <summary>
/// Display chip representing an event reminder.
/// </summary>
public sealed class ReminderChipViewModel
{
    public int Minutes { get; }
    public string Label { get; }
    public IRelayCommand RemoveCommand { get; }

    public ReminderChipViewModel(int minutes, string label, Action<int> onRemove)
    {
        Minutes = minutes;
        Label = label;
        RemoveCommand = new RelayCommand(() => onRemove(minutes));
    }
}

/// <summary>
/// View model powering the event editor dialog (Figma S10, FR-UI-4, FR-LUNAR-5).
/// </summary>
public sealed partial class EventEditorViewModel : ObservableObject
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly IDisplayPreferences _preferences;
    private readonly IMutationService? _mutationService;

    /// <summary>Raised when an event is saved (created or updated) successfully.</summary>
    public event EventHandler<CalendarEvent>? Saved;

    /// <summary>Raised when an event is deleted successfully.</summary>
    public event EventHandler<string>? Deleted;

    /// <summary>Raised when dialog editing is cancelled or closed.</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string? _eventId;

    [ObservableProperty]
    private string? _googleId;

    [ObservableProperty]
    private string? _originalEtag;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private DateOnly _startDate = DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    private TimeOnly _startTime = new(9, 0);

    [ObservableProperty]
    private DateOnly _endDate = DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    private TimeOnly _endTime = new(10, 0);

    [ObservableProperty]
    private bool _isAllDay;

    [ObservableProperty]
    private string _lunarDateText = string.Empty;

    [ObservableProperty]
    private CalendarItemViewModel? _selectedCalendar;

    [ObservableProperty]
    private EventRecurrenceType _recurrenceKind = EventRecurrenceType.None;

    [ObservableProperty]
    private int _lunarDay = 1;

    [ObservableProperty]
    private int _lunarMonth = 1;

    [ObservableProperty]
    private bool _isLunarLeap;

    [ObservableProperty]
    private int _lunarYearsAhead = 5;

    [ObservableProperty]
    private bool _syncToGoogle;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Available calendars list for the target calendar picker.</summary>
    public ObservableCollection<CalendarItemViewModel> AvailableCalendars { get; } = new();

    /// <summary>Active reminder offsets in minutes before event start.</summary>
    public ObservableCollection<int> Reminders { get; } = new();

    /// <summary>Observable collection of reminder chips with remove commands.</summary>
    public ObservableCollection<ReminderChipViewModel> ReminderChips { get; } = new();

    /// <summary>Available recurrence options for the dropdown selector.</summary>
    public IReadOnlyList<RecurrenceOption> RecurrenceOptions => new[]
    {
        new RecurrenceOption(EventRecurrenceType.None, _localizationService["recurrence.none"]),
        new RecurrenceOption(EventRecurrenceType.Daily, _localizationService["recurrence.daily"]),
        new RecurrenceOption(EventRecurrenceType.Weekly, _localizationService["recurrence.weekly"]),
        new RecurrenceOption(EventRecurrenceType.Monthly, _localizationService["recurrence.monthly"]),
        new RecurrenceOption(EventRecurrenceType.Yearly, _localizationService["recurrence.yearly"]),
        new RecurrenceOption(EventRecurrenceType.Lunar, _localizationService["recurrence.lunar"]),
    };

    /// <summary>Selected recurrence option.</summary>
    public RecurrenceOption? SelectedRecurrenceOption
    {
        get => RecurrenceOptions.FirstOrDefault(o => o.Type == RecurrenceKind);
        set
        {
            if (value is not null && RecurrenceKind != value.Type)
            {
                RecurrenceKind = value.Type;
                OnPropertyChanged(nameof(SelectedRecurrenceOption));
            }
        }
    }

    /// <summary>True when editing an existing event.</summary>
    public bool IsEditing => !IsNew;

    /// <summary>True when event is not all-day and has specific start and end times.</summary>
    public bool IsTimed => !IsAllDay;

    /// <summary>True when recurrence type is set to lunar.</summary>
    public bool IsLunarRecurrence => RecurrenceKind == EventRecurrenceType.Lunar;

    /// <summary>True when there is an active validation error message.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Dialog header title (localized).</summary>
    public string DialogTitle => IsNew
        ? _localizationService["event.dialog.create_title"]
        : _localizationService["event.dialog.edit_title"];

    public EventEditorViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        IDisplayPreferences preferences,
        IMutationService? mutationService = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _mutationService = mutationService;

        _preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IDisplayPreferences.LunarEnabled) or nameof(IDisplayPreferences.TimeZone))
            {
                UpdateLunarDateText();
            }
        };

        _localizationService.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DialogTitle));
            OnPropertyChanged(nameof(RecurrenceOptions));
            OnPropertyChanged(nameof(SelectedRecurrenceOption));
            UpdateLunarDateText();
            RefreshReminderChips();
        };

        UpdateLunarDateText();
    }

    partial void OnStartDateChanged(DateOnly value)
    {
        if (EndDate < value)
        {
            EndDate = value;
        }

        UpdateLunarDateText();
    }

    partial void OnRecurrenceKindChanged(EventRecurrenceType value)
    {
        OnPropertyChanged(nameof(IsLunarRecurrence));
        OnPropertyChanged(nameof(SelectedRecurrenceOption));
        if (value == EventRecurrenceType.Lunar)
        {
            var lunar = LunarCalendar.ToLunar(StartDate);
            LunarDay = lunar.Day;
            LunarMonth = lunar.Month;
            IsLunarLeap = lunar.IsLeapMonth;
        }
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsNewChanged(bool value)
    {
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(IsEditing));
    }

    partial void OnIsAllDayChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimed));
    }

    /// <summary>
    /// Loads user calendars into the selectable calendar list.
    /// </summary>
    public async Task LoadCalendarsAsync(CancellationToken cancellationToken = default)
    {
        var calendars = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        AvailableCalendars.Clear();
        foreach (var cal in calendars)
        {
            var item = new CalendarItemViewModel
            {
                Id = cal.Id,
                AccountId = cal.AccountId,
                Summary = cal.Summary,
                Color = cal.Color ?? CalendarColors.DefaultHex,
                IsSelected = cal.IsSelected
            };
            AvailableCalendars.Add(item);
        }

        if (SelectedCalendar is null && AvailableCalendars.Count > 0)
        {
            SelectedCalendar = AvailableCalendars[0];
        }
    }

    /// <summary>
    /// Initializes state according to an <see cref="EventEditorRequest"/>.
    /// </summary>
    public void Initialize(EventEditorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Mode)
        {
            case EventEditorMode.Edit:
                if (request.Event is not null)
                {
                    InitializeForEdit(request.Event);
                }
                else
                {
                    InitializeForNew(request.Date, request.Time, request.IsAllDay, request.CalendarId);
                }
                break;
            case EventEditorMode.CreateLunarRule:
                InitializeForLunarRule(request.Date ?? CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, _preferences.TimeZone), request.CalendarId);
                break;
            default:
                InitializeForNew(request.Date, request.Time, request.IsAllDay, request.CalendarId);
                break;
        }
    }

    /// <summary>
    /// Initializes state for creating a new event (Figma S10).
    /// </summary>
    public void InitializeForNew(
        DateOnly? defaultDate = null,
        TimeOnly? defaultTime = null,
        bool isAllDay = false,
        string? defaultCalendarId = null)
    {
        IsNew = true;
        EventId = Guid.NewGuid().ToString("D");
        GoogleId = null;
        OriginalEtag = null;
        Title = string.Empty;
        Location = string.Empty;
        Description = string.Empty;
        ErrorMessage = null;

        var targetDate = defaultDate ?? CalendarTime.ToLocalDate(DateTimeOffset.UtcNow, _preferences.TimeZone);
        StartDate = targetDate;
        EndDate = targetDate;

        if (defaultTime.HasValue)
        {
            StartTime = defaultTime.Value;
            EndTime = defaultTime.Value.AddHours(1);
        }
        else
        {
            var now = DateTime.Now;
            var startMin = now.Minute >= 30 ? 0 : 30;
            var startHour = now.Minute >= 30 ? (now.Hour + 1) % 24 : now.Hour;
            StartTime = new TimeOnly(startHour, startMin);
            EndTime = StartTime.AddHours(1);
        }

        IsAllDay = isAllDay;
        RecurrenceKind = EventRecurrenceType.None;
        Reminders.Clear();
        Reminders.Add(10); // default 10 minutes reminder
        RefreshReminderChips();

        if (!string.IsNullOrEmpty(defaultCalendarId))
        {
            var found = AvailableCalendars.FirstOrDefault(c => c.Id == defaultCalendarId);
            if (found is not null)
            {
                SelectedCalendar = found;
            }
        }
        else if (SelectedCalendar is null && AvailableCalendars.Count > 0)
        {
            SelectedCalendar = AvailableCalendars[0];
        }

        UpdateLunarDateText();
    }

    /// <summary>
    /// Initializes state for editing an existing event.
    /// </summary>
    public void InitializeForEdit(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        IsNew = false;
        EventId = calendarEvent.Id;
        GoogleId = calendarEvent.GoogleId;
        OriginalEtag = calendarEvent.Etag;
        Title = calendarEvent.Title;
        Location = calendarEvent.Location ?? string.Empty;
        Description = calendarEvent.Description ?? string.Empty;
        IsAllDay = calendarEvent.AllDay;
        ErrorMessage = null;

        var timeZone = _preferences.TimeZone;
        var startLocal = CalendarTime.ToLocal(calendarEvent.StartUtc, timeZone);
        var endLocal = CalendarTime.ToLocal(calendarEvent.EndUtc, timeZone);

        StartDate = DateOnly.FromDateTime(startLocal.DateTime);
        StartTime = TimeOnly.FromDateTime(startLocal.DateTime);
        EndDate = DateOnly.FromDateTime(endLocal.DateTime);
        EndTime = TimeOnly.FromDateTime(endLocal.DateTime);

        // Check lunar recurrence in extended properties
        if (calendarEvent.ExtendedProperties.TryGetValue("lunarRuleId", out var ruleId) && !string.IsNullOrEmpty(ruleId))
        {
            RecurrenceKind = EventRecurrenceType.Lunar;
            if (calendarEvent.ExtendedProperties.TryGetValue("lunarDay", out var dStr) && int.TryParse(dStr, out var d))
            {
                LunarDay = d;
            }

            if (calendarEvent.ExtendedProperties.TryGetValue("lunarMonth", out var mStr) && int.TryParse(mStr, out var m))
            {
                LunarMonth = m;
            }

            if (calendarEvent.ExtendedProperties.TryGetValue("isLeap", out var lStr) && bool.TryParse(lStr, out var leap))
            {
                IsLunarLeap = leap;
            }

            if (calendarEvent.ExtendedProperties.TryGetValue("syncToGoogle", out var syncStr) && bool.TryParse(syncStr, out var sync))
            {
                SyncToGoogle = sync;
            }
        }
        else
        {
            RecurrenceKind = EventRecurrenceType.None;
        }

        Reminders.Clear();
        Reminders.Add(10);
        RefreshReminderChips();

        var cal = AvailableCalendars.FirstOrDefault(c => c.Id == calendarEvent.CalendarId);
        if (cal is not null)
        {
            SelectedCalendar = cal;
        }

        UpdateLunarDateText();
    }

    /// <summary>
    /// Initializes state for creating a new lunar recurrence rule (FR-LUNAR-4, FR-LUNAR-5).
    /// </summary>
    public void InitializeForLunarRule(DateOnly solarDate, string? defaultCalendarId = null)
    {
        InitializeForNew(solarDate, isAllDay: true, defaultCalendarId: defaultCalendarId);
        RecurrenceKind = EventRecurrenceType.Lunar;
        var lunar = LunarCalendar.ToLunar(solarDate);
        LunarDay = lunar.Day;
        LunarMonth = lunar.Month;
        IsLunarLeap = lunar.IsLeapMonth;
    }

    /// <summary>
    /// Adds a reminder offset in minutes.
    /// </summary>
    [RelayCommand]
    public void AddReminder(int minutes)
    {
        if (minutes >= 0 && !Reminders.Contains(minutes))
        {
            Reminders.Add(minutes);
            RefreshReminderChips();
        }
    }

    /// <summary>
    /// Removes a reminder offset in minutes.
    /// </summary>
    [RelayCommand]
    public void RemoveReminder(int minutes)
    {
        if (Reminders.Remove(minutes))
        {
            RefreshReminderChips();
        }
    }

    /// <summary>
    /// Validates the form input (FR-UI-4).
    /// </summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = _localizationService["event.validation.title_required"];
            return false;
        }

        if (!IsAllDay)
        {
            var startUtc = CalendarTime.ToUtc(StartDate, StartTime, _preferences.TimeZone);
            var endUtc = CalendarTime.ToUtc(EndDate, EndTime, _preferences.TimeZone);
            if (endUtc <= startUtc)
            {
                ErrorMessage = _localizationService["event.validation.invalid_time"];
                return false;
            }
        }

        ErrorMessage = null;
        return true;
    }

    /// <summary>
    /// Saves the event optimistically via <see cref="IMutationService"/> (FR-UI-4, FR-SYNC-4, NFR-REL).
    /// </summary>
    [RelayCommand]
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!Validate())
        {
            return false;
        }

        if (_mutationService is null)
        {
            return false;
        }

        var timeZone = _preferences.TimeZone;
        DateTimeOffset startUtc;
        DateTimeOffset endUtc;

        if (IsAllDay)
        {
            startUtc = CalendarTime.StartOfDayUtc(StartDate, timeZone);
            endUtc = CalendarTime.StartOfDayUtc(EndDate.AddDays(1), timeZone);
        }
        else
        {
            startUtc = CalendarTime.ToUtc(StartDate, StartTime, timeZone);
            endUtc = CalendarTime.ToUtc(EndDate, EndTime, timeZone);
        }

        var calendarId = SelectedCalendar?.Id ?? "primary";
        var accountId = SelectedCalendar?.AccountId ?? "primary";
        var eventId = EventId ?? Guid.NewGuid().ToString("D");

        var extended = new Dictionary<string, string>(StringComparer.Ordinal);
        if (IsLunarRecurrence)
        {
            extended["lunarRuleId"] = Guid.NewGuid().ToString("D");
            extended["lunarDay"] = LunarDay.ToString(System.Globalization.CultureInfo.InvariantCulture);
            extended["lunarMonth"] = LunarMonth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            extended["isLeap"] = IsLunarLeap.ToString();
            extended["syncToGoogle"] = SyncToGoogle.ToString();
        }

        var calendarEvent = new CalendarEvent
        {
            Id = eventId,
            CalendarId = calendarId,
            GoogleId = GoogleId,
            Title = Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = IsAllDay,
            TimeZone = timeZone.Id,
            Etag = OriginalEtag,
            Status = "confirmed",
            ExtendedProperties = extended
        };

        if (IsNew)
        {
            var created = await _mutationService.CreateEventOptimisticAsync(accountId, calendarId, calendarEvent, cancellationToken).ConfigureAwait(false);
            Saved?.Invoke(this, created);
        }
        else
        {
            var updated = await _mutationService.UpdateEventOptimisticAsync(accountId, calendarId, calendarEvent, cancellationToken).ConfigureAwait(false);
            Saved?.Invoke(this, updated);
        }

        return true;
    }

    /// <summary>
    /// Deletes the event optimistically via <see cref="IMutationService"/> (FR-UI-4, FR-SYNC-4, NFR-REL).
    /// </summary>
    [RelayCommand]
    public async Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (_mutationService is null || IsNew || string.IsNullOrEmpty(EventId))
        {
            return false;
        }

        var calendarId = SelectedCalendar?.Id ?? "primary";
        var accountId = SelectedCalendar?.AccountId ?? "primary";

        await _mutationService.DeleteEventOptimisticAsync(accountId, calendarId, EventId, OriginalEtag, cancellationToken).ConfigureAwait(false);
        Deleted?.Invoke(this, EventId);
        return true;
    }

    /// <summary>
    /// Duplicates the current event as a new event (Figma S10 context menu).
    /// </summary>
    [RelayCommand]
    public async Task<CalendarEvent?> DuplicateAsync(CancellationToken cancellationToken = default)
    {
        if (_mutationService is null || string.IsNullOrEmpty(Title))
        {
            return null;
        }

        var timeZone = _preferences.TimeZone;
        var startUtc = IsAllDay
            ? CalendarTime.StartOfDayUtc(StartDate.AddDays(1), timeZone)
            : CalendarTime.ToUtc(StartDate, StartTime.AddHours(1), timeZone);
        var endUtc = IsAllDay
            ? CalendarTime.StartOfDayUtc(EndDate.AddDays(2), timeZone)
            : CalendarTime.ToUtc(EndDate, EndTime.AddHours(1), timeZone);

        var calendarId = SelectedCalendar?.Id ?? "primary";
        var accountId = SelectedCalendar?.AccountId ?? "primary";

        var copy = new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("D"),
            CalendarId = calendarId,
            Title = Title.Trim(),
            Description = Description,
            Location = Location,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = IsAllDay,
            TimeZone = timeZone.Id,
            Status = "confirmed"
        };

        return await _mutationService.CreateEventOptimisticAsync(accountId, calendarId, copy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels dialog editing and raises <see cref="Cancelled"/>.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshReminderChips()
    {
        ReminderChips.Clear();
        foreach (var m in Reminders.OrderBy(m => m))
        {
            var label = FormatReminder(m);
            ReminderChips.Add(new ReminderChipViewModel(m, label, RemoveReminder));
        }
    }

    private string FormatReminder(int minutes)
    {
        if (minutes <= 0)
        {
            return _localizationService["reminder.at_start"];
        }
        if (minutes % 1440 == 0)
        {
            return _localizationService.Format("reminder.days_before", minutes / 1440);
        }
        if (minutes % 60 == 0)
        {
            return _localizationService.Format("reminder.hours_before", minutes / 60);
        }
        return _localizationService.Format("reminder.minutes_before", minutes);
    }

    private void UpdateLunarDateText()
    {
        if (!_preferences.LunarEnabled)
        {
            LunarDateText = string.Empty;
            return;
        }

        try
        {
            var lunar = LunarCalendar.ToLunar(StartDate);
            var leapSuffix = lunar.IsLeapMonth ? " " + _localizationService["lunar.leap_marker"] : string.Empty;
            LunarDateText = $"{lunar.Day}/{lunar.Month}{leapSuffix} {_localizationService["lunar.label"]}";
        }
        catch
        {
            LunarDateText = string.Empty;
        }
    }
}
