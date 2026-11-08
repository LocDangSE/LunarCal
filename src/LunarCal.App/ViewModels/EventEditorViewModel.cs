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
/// Display item representing an event time zone choice (Figma 52:971, S10 3.4).
/// </summary>
public sealed record EventTimeZoneOption(string Id, string Label, bool IsDefault, TimeZoneInfo TimeZone);

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

/// <summary>One entry of the "+ Thêm nhắc nhở" menu: a reminder offset that can be added with one click.</summary>
public sealed class ReminderPreset
{
    public ReminderPreset(int minutes, string label, Action<int> onAdd)
    {
        Minutes = minutes;
        Label = label;
        AddCommand = new RelayCommand(() => onAdd(minutes));
    }

    public int Minutes { get; }
    public string Label { get; }
    public IRelayCommand AddCommand { get; }
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

    // Offsets offered by "+ Thêm nhắc nhở": at start, 10 min, 30 min, 1 h, 1 day.
    private static readonly int[] _presetMinutes = [0, 10, 30, 60, 1440];

    // Frequency used when the "Dương lịch" segment is chosen.
    private EventRecurrenceType _solarFrequency = EventRecurrenceType.Weekly;

    // RRULE lines of the event being edited. Kept as they are while the user leaves the solar frequency alone, so a rule
    // the editor cannot express (BYDAY, INTERVAL, COUNT, UNTIL, EXDATE, ...) is not rewritten into a plain FREQ rule.
    private IReadOnlyList<string>? _preservedRecurrence;

    // True while editing an event that uses the calendar's default reminders (ReminderMinutes == null) and the user has
    // not touched the reminder chips: saving then keeps "use default" instead of writing an empty list (= no reminder).
    private bool _usesDefaultReminders;

    // The kind the recurrence had when the editor was initialised; _preservedRecurrence applies only while it is unchanged.
    private EventRecurrenceType _preservedKind = EventRecurrenceType.None;

    private string? _lunarRuleId;

    // Calendar of the event being edited, so deleting it does not depend on the picker.
    private string? _originalCalendarId;

    private DateTimeOffset? _updated;

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

    /// <summary>Available time zones for this event (Figma 52:971).</summary>
    public ObservableCollection<EventTimeZoneOption> TimeZoneOptions { get; } = new();

    [ObservableProperty]
    private EventTimeZoneOption? _selectedTimeZoneOption;

    /// <summary>Effective time zone for the event.</summary>
    public TimeZoneInfo SelectedTimeZoneInfo => SelectedTimeZoneOption?.TimeZone ?? _preferences.TimeZone;

    /// <summary>True when the user explicitly opened the end date picker via "Ngày kết thúc khác…".</summary>
    [ObservableProperty]
    private bool _isCustomEndDateOpen;


    /// <summary>True when a timed event extends past its start date (e.g. 22:00 -> 01:00 = overnight).</summary>
    public bool IsOvernight => IsTimed && EndDate > StartDate;

    /// <summary>Overnight chip label: "+1 ngày · kết thúc 01:00" (Figma 52:370, S10 3.3).</summary>
    public string OvernightChipText
    {
        get
        {
            if (!IsOvernight)
            {
                return string.Empty;
            }

            var diff = EndDate.DayNumber - StartDate.DayNumber;
            return _localizationService.Format("event.overnight_chip", diff, EndTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Headline describing the solar recurrence (e.g. "Lặp vào thứ Năm hàng tuần").</summary>
    public string SolarRuleTitle
    {
        get
        {
            return _solarFrequency switch
            {
                EventRecurrenceType.Daily => _localizationService["recurrence.solar_rule.daily"],
                EventRecurrenceType.Weekly => _localizationService.Format("recurrence.solar_rule.weekly", FormatWeekday(StartDate)),
                EventRecurrenceType.Monthly => _localizationService.Format("recurrence.solar_rule.monthly", StartDate.Day),
                EventRecurrenceType.Yearly => _localizationService.Format("recurrence.solar_rule.yearly", StartDate.Day, StartDate.Month),
                _ => string.Empty,
            };
        }
    }

    /// <summary>Next occurrences for the solar recurrence (e.g. "Lần tới: 29/10/2026 · 05/11/2026 · 12/11/2026").</summary>
    public string SolarRuleNextText
    {
        get
        {
            var dates = NextSolarOccurrences(3);
            if (dates.Count == 0)
            {
                return string.Empty;
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return _localizationService.Format("recurrence.solar_rule.next", string.Join(" · ", dates.Select(d => d.ToString("dd/MM/yyyy", culture))));
        }
    }

    /// <summary>Available calendars list for the target calendar picker.</summary>
    public ObservableCollection<CalendarItemViewModel> AvailableCalendars { get; } = new();

    /// <summary>Active reminder offsets in minutes before event start.</summary>
    public ObservableCollection<int> Reminders { get; } = new();

    /// <summary>Observable collection of reminder chips with remove commands.</summary>
    public ObservableCollection<ReminderChipViewModel> ReminderChips { get; } = new();

    /// <summary>Solar frequencies offered once "Dương lịch" is chosen in the recurrence segmented control (Figma S10).</summary>
    public IReadOnlyList<RecurrenceOption> SolarFrequencyOptions => new[]
    {
        new RecurrenceOption(EventRecurrenceType.Daily, _localizationService["recurrence.daily"]),
        new RecurrenceOption(EventRecurrenceType.Weekly, _localizationService["recurrence.weekly"]),
        new RecurrenceOption(EventRecurrenceType.Monthly, _localizationService["recurrence.monthly"]),
        new RecurrenceOption(EventRecurrenceType.Yearly, _localizationService["recurrence.yearly"]),
    };

    /// <summary>Selected solar frequency; setting it while the solar segment is active also changes the recurrence.</summary>
    public RecurrenceOption? SelectedSolarFrequencyOption
    {
        get => SolarFrequencyOptions.FirstOrDefault(o => o.Type == _solarFrequency);
        set
        {
            if (value is null || value.Type == _solarFrequency)
            {
                return;
            }

            _solarFrequency = value.Type;
            if (IsSolarRecurrence)
            {
                RecurrenceKind = value.Type;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SolarRuleTitle));
            OnPropertyChanged(nameof(SolarRuleNextText));
        }
    }

    /// <summary>"Không lặp" segment (Figma S10).</summary>
    public bool IsRecurrenceNone
    {
        get => RecurrenceKind == EventRecurrenceType.None;
        set
        {
            if (value)
            {
                RecurrenceKind = EventRecurrenceType.None;
            }
        }
    }

    /// <summary>"Dương lịch" segment: any of the solar frequencies.</summary>
    public bool IsSolarRecurrence
    {
        get => RecurrenceKind is EventRecurrenceType.Daily or EventRecurrenceType.Weekly
            or EventRecurrenceType.Monthly or EventRecurrenceType.Yearly;
        set
        {
            if (value && !IsSolarRecurrence)
            {
                RecurrenceKind = _solarFrequency;
            }
        }
    }

    /// <summary>"Âm lịch" segment: yearly or monthly rule on a lunar day (FR-LUNAR-4).</summary>
    public bool IsLunarRecurrence
    {
        get => RecurrenceKind == EventRecurrenceType.Lunar;
        set
        {
            if (value)
            {
                RecurrenceKind = EventRecurrenceType.Lunar;
            }
        }
    }

    /// <summary>True when editing an existing event.</summary>
    public bool IsEditing => !IsNew;

    /// <summary>True when event is not all-day and has specific start and end times.</summary>
    public bool IsTimed => !IsAllDay;

    /// <summary>
    /// True when the end date needs its own picker: all-day events, custom end date opened,
    /// or timed events that span past overnight (> 1 day).
    /// </summary>
    public bool ShowEndDate
    {
        get
        {
            if (IsAllDay)
            {
                return true;
            }

            if (IsCustomEndDateOpen)
            {
                return true;
            }

            if (IsTimed)
            {
                if (EndDate == StartDate.AddDays(1) && EndTime <= StartTime)
                {
                    return false;
                }

                return EndDate != StartDate;
            }

            return false;
        }
    }

    /// <summary>Opens the end date picker for choosing a different end date (Figma 52:370).</summary>
    [RelayCommand]
    public void OpenCustomEndDate()
    {
        IsCustomEndDateOpen = true;
        OnPropertyChanged(nameof(ShowEndDate));
    }

    /// <summary>Half-hour time slots offered by the time drop-downs (Figma S10 "10:00 ▾").</summary>
    public IReadOnlyList<string> TimeSlots { get; } =
        Enumerable.Range(0, 48)
            .Select(i => new TimeOnly(i / 2, i % 2 * 30).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    /// <summary>When the lunar-rule fields (day, month, leap, years ahead) are expanded ("Tuỳ chỉnh luật lặp âm lịch…").</summary>
    [ObservableProperty]
    private bool _isLunarRuleCustomizing;

    /// <summary>True when there is an active validation error message.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Dialog header title (localized).</summary>
    public string DialogTitle => IsNew
        ? _localizationService["event.dialog.create_title"]
        : _localizationService["event.dialog.edit_title"];

    /// <summary>Header sub line: calendar name, plus "sửa lần cuối …" when editing an event with a known update time.</summary>
    public string DialogSubtitle
    {
        get
        {
            var parts = new List<string>();
            if (SelectedCalendar is not null)
            {
                parts.Add(SelectedCalendar.Summary);
            }

            if (IsEditing && _updated is { } updated)
            {
                parts.Add(_localizationService.Format("event.subtitle.edited", GoogleAccountViewModel.FormatRelativeTime(updated, _localizationService)));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Time zone shown next to the all-day check box: "(GMT+07:00) Bangkok, Hanoi, Jakarta" (Figma S10).</summary>
    public string TimeZoneText
    {
        get
        {
            var zone = _preferences.TimeZone;
            var offset = zone.GetUtcOffset(CalendarTime.ToUtc(StartDate, StartTime, zone));
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var abs = offset.Duration();
            var name = zone.DisplayName;

            // Windows names read "(UTC+07:00) Bangkok, Hanoi, Jakarta": the offset is already shown, so drop that prefix.
            var close = name.IndexOf(')', StringComparison.Ordinal);
            if (name.StartsWith('(') && close > 0)
            {
                name = name[(close + 1)..].TrimStart();
            }

            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"(GMT{sign}{abs.Hours:D2}:{abs.Minutes:D2}) {name}");
        }
    }

    /// <summary>Start date with weekday for the date field: "Thứ Năm, 22/10/2026" (Figma S10).</summary>
    public string StartDateText => FormatDateWithWeekday(StartDate);

    /// <summary>End date with weekday for the end-date field.</summary>
    public string EndDateText => FormatDateWithWeekday(EndDate);

    /// <summary>Lunar date chip inside the start-date field: "13/9 ÂL"; empty when the lunar calendar is off.</summary>
    public string StartLunarChipText
    {
        get
        {
            if (!_preferences.LunarEnabled)
            {
                return string.Empty;
            }

            try
            {
                var lunar = LunarCalendar.ToLunar(StartDate);
                var leap = lunar.IsLeapMonth ? _localizationService["lunar.leap_marker"] : string.Empty;
                return $"{lunar.Day}/{lunar.Month}{leap} {_localizationService["lunar.abbrev"]}";
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Lunar-rule headline: "Hằng năm vào ngày 13 tháng 9 âm lịch".</summary>
    public string LunarRuleTitle => _localizationService.Format("lunar_rule.annual_on", LunarDay, LunarMonthLabel);

    /// <summary>Lunar-rule policy line (leap month, short month, years ahead).</summary>
    public string LunarRulePolicyText => _localizationService.Format("lunar_rule.policy", LunarMonth, LunarYearsAhead);

    /// <summary>"Lần tới: 22/10/2026 · 12/10/2027 · 30/10/2028": the next solar dates of the rule, computed by the lunar engine.</summary>
    public string LunarRuleNextText
    {
        get
        {
            var dates = NextLunarOccurrences(3);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return dates.Count == 0
                ? string.Empty
                : _localizationService.Format("lunar_rule.next", string.Join(" · ", dates.Select(d => d.ToString("dd/MM/yyyy", culture))));
        }
    }

    /// <summary>Label of the "sync to Google" check box: names the Google calendar the rule is written to.</summary>
    public string SyncToGoogleText =>
        _localizationService.Format("lunar_rule.sync_google_to", AvailableCalendars.FirstOrDefault(c => c.IsPrimary)?.Summary ?? SelectedCalendar?.Summary ?? string.Empty);

    /// <summary>Preset reminders offered by "+ Thêm nhắc nhở".</summary>
    public IReadOnlyList<ReminderPreset> ReminderPresets { get; private set; } = Array.Empty<ReminderPreset>();

    private string FormatWeekday(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd", _localizationService.CurrentCulture);

    private List<DateOnly> NextSolarOccurrences(int count)
    {
        var result = new List<DateOnly>();
        var current = StartDate;
        for (int i = 0; i < count; i++)
        {
            current = _solarFrequency switch
            {
                EventRecurrenceType.Daily => current.AddDays(1),
                EventRecurrenceType.Weekly => current.AddDays(7),
                EventRecurrenceType.Monthly => current.AddMonths(1),
                EventRecurrenceType.Yearly => current.AddYears(1),
                _ => current,
            };
            if (current == StartDate)
            {
                break;
            }
            result.Add(current);
        }
        return result;
    }

    private void PopulateTimeZoneOptions()
    {
        var previousSelectedId = SelectedTimeZoneOption?.Id;
        TimeZoneOptions.Clear();

        var prefZone = _preferences.TimeZone;
        var defaultLabel = _localizationService.Format("event.timezone.default", FormatTimeZoneOffsetAndName(prefZone));
        var defaultOption = new EventTimeZoneOption(string.Empty, defaultLabel, IsDefault: true, prefZone);
        TimeZoneOptions.Add(defaultOption);

        var utcLabel = FormatTimeZoneOffsetAndName(TimeZoneInfo.Utc);
        TimeZoneOptions.Add(new EventTimeZoneOption("UTC", $"UTC ({utcLabel})", IsDefault: false, TimeZoneInfo.Utc));

        var systemZones = TimeZoneInfo.GetSystemTimeZones()
            .Where(z => z.Id != TimeZoneInfo.Utc.Id && z.Id != prefZone.Id)
            .OrderBy(z => z.BaseUtcOffset)
            .ThenBy(z => z.DisplayName);

        foreach (var zone in systemZones)
        {
            TimeZoneOptions.Add(new EventTimeZoneOption(zone.Id, FormatTimeZoneOffsetAndName(zone), IsDefault: false, zone));
        }

        if (!string.IsNullOrEmpty(previousSelectedId))
        {
            SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(o => o.Id == previousSelectedId) ?? defaultOption;
        }
        else
        {
            SelectedTimeZoneOption = defaultOption;
        }
    }

    public static string FormatTimeZoneOffsetAndName(TimeZoneInfo zone)
    {
        var offset = zone.BaseUtcOffset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var name = zone.DisplayName;
        var close = name.IndexOf(')', StringComparison.Ordinal);
        if (name.StartsWith('(') && close > 0)
        {
            name = name[(close + 1)..].TrimStart();
        }
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"(GMT{sign}{abs.Hours:D2}:{abs.Minutes:D2}) {name}");
    }

    private string FormatDateWithWeekday(DateOnly date) =>
        _localizationService.Format("format.date_weekday", date.ToDateTime(TimeOnly.MinValue));

    private string LunarMonthLabel => CalendarText.MonthLabel(_localizationService, new LunarDate(0, LunarMonth, LunarDay, IsLunarLeap));

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
                OnPropertyChanged(nameof(TimeZoneText));
                OnPropertyChanged(nameof(StartLunarChipText));
            }
        };

        _localizationService.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DialogTitle));
            OnPropertyChanged(nameof(DialogSubtitle));
            OnPropertyChanged(nameof(SolarFrequencyOptions));
            OnPropertyChanged(nameof(SelectedSolarFrequencyOption));
            OnPropertyChanged(nameof(SyncToGoogleText));
            OnPropertyChanged(nameof(StartDateText));
            OnPropertyChanged(nameof(EndDateText));
            OnPropertyChanged(nameof(StartLunarChipText));
            OnPropertyChanged(nameof(SolarRuleTitle));
            OnPropertyChanged(nameof(SolarRuleNextText));
            OnPropertyChanged(nameof(OvernightChipText));
            PopulateTimeZoneOptions();
            NotifyLunarRuleChanged();
            UpdateLunarDateText();
            RefreshReminderChips();
        };

        AvailableCalendars.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SyncToGoogleText));

        PopulateTimeZoneOptions();
        UpdateLunarDateText();
        RefreshReminderChips();
    }

    partial void OnStartTimeChanged(TimeOnly value)
    {
        if (IsTimed && !IsCustomEndDateOpen)
        {
            if (EndTime <= value)
            {
                EndDate = StartDate.AddDays(1);
            }
            else if (EndDate == StartDate.AddDays(1))
            {
                EndDate = StartDate;
            }
        }
        NotifyOvernightChanged();
    }

    partial void OnEndTimeChanged(TimeOnly value)
    {
        if (IsTimed && !IsCustomEndDateOpen)
        {
            if (value <= StartTime)
            {
                EndDate = StartDate.AddDays(1);
            }
            else if (EndDate == StartDate.AddDays(1))
            {
                EndDate = StartDate;
            }
        }
        NotifyOvernightChanged();
    }

    partial void OnStartDateChanged(DateOnly value)
    {
        if (!IsCustomEndDateOpen && IsTimed && EndTime <= StartTime)
        {
            EndDate = value.AddDays(1);
        }
        else if (EndDate < value)
        {
            EndDate = value;
        }

        OnPropertyChanged(nameof(ShowEndDate));
        OnPropertyChanged(nameof(TimeZoneText));
        OnPropertyChanged(nameof(StartDateText));
        OnPropertyChanged(nameof(StartLunarChipText));
        OnPropertyChanged(nameof(SolarRuleTitle));
        OnPropertyChanged(nameof(SolarRuleNextText));
        UpdateLunarDateText();

        // Until "Tuỳ chỉnh" is opened the rule simply follows the start date (Figma S10 derives it from the date).
        if (IsLunarRecurrence && !IsLunarRuleCustomizing)
        {
            SyncLunarRuleToStartDate();
        }

        OnPropertyChanged(nameof(LunarRuleNextText));
        NotifyOvernightChanged();
    }

    partial void OnEndDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(ShowEndDate));
        OnPropertyChanged(nameof(EndDateText));
        NotifyOvernightChanged();
    }

    private void NotifyOvernightChanged()
    {
        OnPropertyChanged(nameof(IsOvernight));
        OnPropertyChanged(nameof(OvernightChipText));
        OnPropertyChanged(nameof(ShowEndDate));
    }

    partial void OnSelectedCalendarChanged(CalendarItemViewModel? value)
    {
        OnPropertyChanged(nameof(DialogSubtitle));
        OnPropertyChanged(nameof(SyncToGoogleText));
    }

    partial void OnLunarDayChanged(int value) => NotifyLunarRuleChanged();

    partial void OnLunarMonthChanged(int value) => NotifyLunarRuleChanged();

    partial void OnIsLunarLeapChanged(bool value) => NotifyLunarRuleChanged();

    partial void OnLunarYearsAheadChanged(int value) => NotifyLunarRuleChanged();

    partial void OnRecurrenceKindChanged(EventRecurrenceType value)
    {
        OnPropertyChanged(nameof(IsRecurrenceNone));
        OnPropertyChanged(nameof(IsSolarRecurrence));
        OnPropertyChanged(nameof(IsLunarRecurrence));
        if (value is EventRecurrenceType.Daily or EventRecurrenceType.Weekly or EventRecurrenceType.Monthly or EventRecurrenceType.Yearly)
        {
            _solarFrequency = value;
            OnPropertyChanged(nameof(SelectedSolarFrequencyOption));
        }

        if (value == EventRecurrenceType.Lunar)
        {
            SyncLunarRuleToStartDate();
        }
    }

    private void SyncLunarRuleToStartDate()
    {
        try
        {
            var lunar = LunarCalendar.ToLunar(StartDate);
            LunarDay = lunar.Day;
            LunarMonth = lunar.Month;
            IsLunarLeap = lunar.IsLeapMonth;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Outside 1900..2100 there is no lunar date; the rule keeps its previous values.
        }
    }

    private void NotifyLunarRuleChanged()
    {
        OnPropertyChanged(nameof(LunarRuleTitle));
        OnPropertyChanged(nameof(LunarRulePolicyText));
        OnPropertyChanged(nameof(LunarRuleNextText));
    }

    private List<DateOnly> NextLunarOccurrences(int count)
    {
        var result = new List<DateOnly>();
        if (LunarMonth is < 1 or > 12 || LunarDay is < 1 or > 30)
        {
            return result;
        }

        try
        {
            for (var year = LunarCalendar.ToLunar(StartDate).Year; year <= LunarCalendar.MaxLunarYear && result.Count < count; year++)
            {
                // Short month: day 30 falls back to the last day of the month (the rule's stated policy).
                var solar = LunarCalendar.ToSolar(year, LunarMonth, LunarDay, IsLunarLeap)
                    ?? (LunarDay == 30 ? LunarCalendar.ToSolar(year, LunarMonth, 29, IsLunarLeap) : null);
                if (solar is { } date && date >= StartDate)
                {
                    result.Add(date);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Past the supported range (solar 2100-12-31): list what was found.
        }

        return result;
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsNewChanged(bool value)
    {
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(DialogSubtitle));
        OnPropertyChanged(nameof(IsEditing));
    }

    partial void OnIsAllDayChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTimed));
        OnPropertyChanged(nameof(ShowEndDate));
        NotifyOvernightChanged();
    }

    /// <summary>
    /// Loads user calendars into the selectable calendar list.
    /// </summary>
    public async Task LoadCalendarsAsync(CancellationToken cancellationToken = default)
    {
        // Back on the caller's context: AvailableCalendars is bound to the dialog's calendar picker.
        var calendars = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        AvailableCalendars.Clear();
        foreach (var cal in calendars)
        {
            var item = new CalendarItemViewModel
            {
                Id = cal.Id,
                AccountId = cal.AccountId,
                Summary = cal.Summary,
                Color = cal.Color ?? CalendarColors.DefaultHex,
                IsSelected = cal.IsSelected,
                IsPrimary = cal.IsPrimary,
            };
            AvailableCalendars.Add(item);
        }

        if (SelectedCalendar is null && AvailableCalendars.Count > 0)
        {
            SelectedCalendar = DefaultCalendar();
        }
    }

    // The primary calendar when there is one, otherwise the first.
    private CalendarItemViewModel? DefaultCalendar() =>
        AvailableCalendars.FirstOrDefault(c => c.IsPrimary) ?? AvailableCalendars.FirstOrDefault();

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

        if (request.Prefill is not null && IsNew)
        {
            ApplyPrefill(request.Prefill);
        }
    }

    /// <summary>
    /// Fills a new event with what the AI proposed so "Sửa" in the confirmation dialog starts from it (FR-CONFIRM-4).
    /// The order matters: the date and time setters adjust the end, so the end is set last.
    /// </summary>
    public void ApplyPrefill(EventEditorPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);

        Title = prefill.Title;
        IsAllDay = prefill.IsAllDay;
        StartDate = DateOnly.FromDateTime(prefill.Start);
        if (!prefill.IsAllDay)
        {
            StartTime = TimeOnly.FromDateTime(prefill.Start);
            EndTime = TimeOnly.FromDateTime(prefill.End);
        }

        // An all-day event proposed by the model ends the day after it starts (exclusive end); the editor shows the last day.
        EndDate = prefill.IsAllDay
            ? DateOnly.FromDateTime(prefill.End).AddDays(prefill.End.TimeOfDay == TimeSpan.Zero && prefill.End > prefill.Start ? -1 : 0)
            : DateOnly.FromDateTime(prefill.End);
        if (EndDate < StartDate)
        {
            EndDate = StartDate;
        }

        NotifyOvernightChanged();
        UpdateLunarDateText();
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
        _updated = null;
        _originalCalendarId = null;
        _preservedRecurrence = null;
        _preservedKind = EventRecurrenceType.None;
        _lunarRuleId = null;
        _usesDefaultReminders = false;
        IsLunarRuleCustomizing = false;
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
        IsCustomEndDateOpen = false;
        SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(o => o.IsDefault) ?? TimeZoneOptions.FirstOrDefault();
        if (EndTime <= StartTime && !isAllDay)
        {
            EndDate = StartDate.AddDays(1);
        }
        NotifyOvernightChanged();

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
            SelectedCalendar = DefaultCalendar();
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
        _updated = calendarEvent.Updated;
        _originalCalendarId = calendarEvent.CalendarId;
        IsLunarRuleCustomizing = false;
        Title = calendarEvent.Title;
        Location = calendarEvent.Location ?? string.Empty;
        Description = calendarEvent.Description ?? string.Empty;
        IsAllDay = calendarEvent.AllDay;
        ErrorMessage = null;

        IsCustomEndDateOpen = false;
        if (!string.IsNullOrEmpty(calendarEvent.TimeZone))
        {
            var matched = TimeZoneOptions.FirstOrDefault(o => o.Id == calendarEvent.TimeZone);
            if (matched is not null)
            {
                SelectedTimeZoneOption = matched;
            }
            else
            {
                try
                {
                    var customZone = TimeZoneInfo.FindSystemTimeZoneById(calendarEvent.TimeZone);
                    var customOpt = new EventTimeZoneOption(customZone.Id, FormatTimeZoneOffsetAndName(customZone), IsDefault: false, customZone);
                    TimeZoneOptions.Add(customOpt);
                    SelectedTimeZoneOption = customOpt;
                }
                catch
                {
                    SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(o => o.IsDefault) ?? TimeZoneOptions.FirstOrDefault();
                }
            }
        }
        else
        {
            SelectedTimeZoneOption = TimeZoneOptions.FirstOrDefault(o => o.IsDefault) ?? TimeZoneOptions.FirstOrDefault();
        }

        var timeZone = SelectedTimeZoneInfo;
        var startLocal = CalendarTime.ToLocal(calendarEvent.StartUtc, timeZone);
        var endLocal = CalendarTime.ToLocal(calendarEvent.EndUtc, timeZone);

        StartDate = DateOnly.FromDateTime(startLocal.DateTime);
        StartTime = TimeOnly.FromDateTime(startLocal.DateTime);
        EndDate = DateOnly.FromDateTime(endLocal.DateTime);
        EndTime = TimeOnly.FromDateTime(endLocal.DateTime);

        _preservedRecurrence = null;
        _lunarRuleId = null;

        // Check lunar recurrence in extended properties
        if (calendarEvent.ExtendedProperties.TryGetValue("lunarRuleId", out var ruleId) && !string.IsNullOrEmpty(ruleId))
        {
            _lunarRuleId = ruleId;
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
        else if (calendarEvent.Recurrence is { Count: > 0 } rules)
        {
            // A recurring master: show its frequency, and keep the original lines unless the user changes the frequency.
            _preservedRecurrence = rules;
            RecurrenceKind = ParseSolarFrequency(rules);
        }
        else
        {
            RecurrenceKind = EventRecurrenceType.None;
        }

        _preservedKind = RecurrenceKind;

        // null = the calendar's default reminders; an explicit list (possibly empty) is shown as chips.
        _usesDefaultReminders = calendarEvent.ReminderMinutes is null;
        Reminders.Clear();
        foreach (var minutes in calendarEvent.ReminderMinutes ?? Array.Empty<int>())
        {
            Reminders.Add(minutes);
        }

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
            _usesDefaultReminders = false;
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
            _usesDefaultReminders = false;
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

        // The Event table has a foreign key to CalendarInfo: an event needs a real calendar, never a made-up "primary".
        if (SelectedCalendar is null)
        {
            ErrorMessage = _localizationService["event.validation.calendar_required"];
            return false;
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

        var timeZone = SelectedTimeZoneInfo;
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

        var calendarId = SelectedCalendar!.Id;
        var accountId = SelectedCalendar.AccountId ?? calendarId;
        var eventId = EventId ?? Guid.NewGuid().ToString("D");

        var extended = new Dictionary<string, string>(StringComparer.Ordinal);
        if (IsLunarRecurrence)
        {
            _lunarRuleId ??= Guid.NewGuid().ToString("D");
            extended["lunarRuleId"] = _lunarRuleId;
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
            ReminderMinutes = _usesDefaultReminders ? null : Reminders.Order().ToList(),
            Recurrence = BuildRecurrence(),
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

        // Delete from the event's own calendar; the picker may have been changed (unsaved) after opening.
        var calendarId = _originalCalendarId ?? SelectedCalendar?.Id;
        if (calendarId is null)
        {
            ErrorMessage = _localizationService["event.validation.calendar_required"];
            return false;
        }

        var accountId = AvailableCalendars.FirstOrDefault(c => c.Id == calendarId)?.AccountId ?? calendarId;

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

        var timeZone = SelectedTimeZoneInfo;
        var startUtc = IsAllDay
            ? CalendarTime.StartOfDayUtc(StartDate.AddDays(1), timeZone)
            : CalendarTime.ToUtc(StartDate, StartTime.AddHours(1), timeZone);
        var endUtc = IsAllDay
            ? CalendarTime.StartOfDayUtc(EndDate.AddDays(2), timeZone)
            : CalendarTime.ToUtc(EndDate, EndTime.AddHours(1), timeZone);

        if (SelectedCalendar is null)
        {
            ErrorMessage = _localizationService["event.validation.calendar_required"];
            return null;
        }

        var calendarId = SelectedCalendar.Id;
        var accountId = SelectedCalendar.AccountId ?? calendarId;

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

    /// <summary>Expands or collapses the lunar-rule fields ("Tuỳ chỉnh luật lặp âm lịch…").</summary>
    [RelayCommand]
    public void ToggleLunarRuleCustomizing() => IsLunarRuleCustomizing = !IsLunarRuleCustomizing;

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
        // Earliest reminder first, as in Figma S10 ("1 ngày trước" before "2 giờ trước").
        foreach (var m in Reminders.OrderByDescending(m => m))
        {
            var label = _localizationService.Format("reminder.chip", FormatReminder(m));
            ReminderChips.Add(new ReminderChipViewModel(m, label, RemoveReminder));
        }

        ReminderPresets = _presetMinutes
            .Where(m => !Reminders.Contains(m))
            .Select(m => new ReminderPreset(m, FormatReminder(m), AddReminder))
            .ToList();
        OnPropertyChanged(nameof(ReminderPresets));
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

    /// <summary>RRULE lines to write for the chosen recurrence; null when the event does not repeat.</summary>
    private IReadOnlyList<string>? BuildRecurrence()
    {
        if (_preservedRecurrence is not null && RecurrenceKind == _preservedKind)
        {
            return _preservedRecurrence;
        }

        var freq = RecurrenceKind switch
        {
            EventRecurrenceType.Daily => "DAILY",
            EventRecurrenceType.Weekly => "WEEKLY",
            EventRecurrenceType.Monthly => "MONTHLY",
            EventRecurrenceType.Yearly => "YEARLY",
            _ => null,
        };

        // Lunar rules are materialised later (FR-LUNAR-4, Phase 5); they carry no RRULE of their own.
        return freq is null ? null : new[] { "RRULE:FREQ=" + freq };
    }

    private static EventRecurrenceType ParseSolarFrequency(IReadOnlyList<string> rules)
    {
        foreach (var line in rules)
        {
            if (!line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var part in line["RRULE:".Length..].Split(';'))
            {
                if (part.StartsWith("FREQ=", StringComparison.OrdinalIgnoreCase))
                {
                    return part["FREQ=".Length..].ToUpperInvariant() switch
                    {
                        "DAILY" => EventRecurrenceType.Daily,
                        "WEEKLY" => EventRecurrenceType.Weekly,
                        "MONTHLY" => EventRecurrenceType.Monthly,
                        "YEARLY" => EventRecurrenceType.Yearly,
                        _ => EventRecurrenceType.None,
                    };
                }
            }
        }

        return EventRecurrenceType.None;
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
