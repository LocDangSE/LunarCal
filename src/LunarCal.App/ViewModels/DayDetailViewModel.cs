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
/// View model controlling the Day Detail side panel (Figma S09, FR-UI-3, FR-I18N-5, FR-LUNAR-8).
/// Displays comprehensive solar/lunar information, Can Chi for day/month/year with English glosses,
/// 24 solar terms, leap month badges, and the day's event schedule.
/// </summary>
public sealed partial class DayDetailViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsStore _settingsStore;

    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private string _solarDateTitle = string.Empty;

    [ObservableProperty]
    private string _solarDateShort = string.Empty;

    // LUNAR METRICS (FR-UI-3, FR-I18N-5, FR-LUNAR-8)
    [ObservableProperty]
    private bool _hasLunarDate;

    [ObservableProperty]
    private int _lunarDay;

    [ObservableProperty]
    private int _lunarMonth;

    [ObservableProperty]
    private int _lunarYear;

    [ObservableProperty]
    private bool _isLeapMonth;

    [ObservableProperty]
    private bool _isFirstLunarDay;

    [ObservableProperty]
    private string _lunarDayNumberText = string.Empty;

    [ObservableProperty]
    private string _lunarMonthText = string.Empty;

    [ObservableProperty]
    private string _dayCanChiText = string.Empty;

    [ObservableProperty]
    private string _monthCanChiText = string.Empty;

    [ObservableProperty]
    private string _yearCanChiText = string.Empty;

    [ObservableProperty]
    private string _solarTermText = string.Empty;

    [ObservableProperty]
    private bool _isSolarTermStartDay;

    [ObservableProperty]
    private string _solarTermDegreesText = string.Empty;

    // EVENTS
    [ObservableProperty]
    private bool _hasEvents;

    /// <summary>List of events occurring on this date, all-day first then ordered by start time.</summary>
    public ObservableCollection<AgendaEventItemViewModel> Events { get; } = new();

    /// <summary>Event raised when the user requests adding an event for this date.</summary>
    public event EventHandler<DateOnly>? AddEventRequested;

    /// <summary>Event raised when the user requests adding a recurring lunar rule for this date.</summary>
    public event EventHandler<DateOnly>? AddLunarRuleRequested;

    /// <summary>Event raised when the user closes the day detail panel.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Initializes a new instance of <see cref="DayDetailViewModel"/>.
    /// </summary>
    public DayDetailViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        _date = DateOnly.FromDateTime(DateTime.Today);
        _localizationService.PropertyChanged += OnLocalizationChanged;

        _ = InitializeSettingsAndLoadAsync();
    }

    private async Task InitializeSettingsAndLoadAsync()
    {
        IsLunarVisible = await _settingsStore.GetBoolAsync(SettingKeys.LunarEnabled, defaultValue: true);
        await LoadDateAsync(Date);
    }

    /// <summary>
    /// Sets whether lunar details are visible in the panel (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        UpdateLunarDisplay();
    }

    /// <summary>
    /// Sets culture and refreshes formatted strings.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        _ = LoadDateAsync(Date);
    }

    /// <summary>
    /// Sets the selected date and loads events and lunar calculations for that day (FR-UI-3).
    /// </summary>
    public async Task SetDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        Date = date;
        await LoadDateAsync(date, cancellationToken);
    }

    private async Task LoadDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        IsToday = date == DateOnly.FromDateTime(DateTime.Today);

        var culture = _localizationService.CurrentCulture;
        var dayOfWeekName = culture.DateTimeFormat.GetDayName(date.DayOfWeek);

        if (culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            SolarDateTitle = $"{dayOfWeekName}, {date.Day} Tháng {date.Month}, {date.Year}";
            SolarDateShort = $"{date.Day} Tháng {date.Month}, {date.Year}";
        }
        else
        {
            var dateTime = date.ToDateTime(TimeOnly.MinValue);
            SolarDateTitle = dateTime.ToString("dddd, MMMM d, yyyy", culture);
            SolarDateShort = dateTime.ToString("MMM d, yyyy", culture);
        }

        UpdateLunarDisplay();
        await LoadEventsAsync(date, cancellationToken);
    }

    private void UpdateLunarDisplay()
    {
        if (Date < LunarCalendar.MinSolarDate || Date > LunarCalendar.MaxSolarDate)
        {
            HasLunarDate = false;
            LunarDayNumberText = string.Empty;
            LunarMonthText = string.Empty;
            DayCanChiText = string.Empty;
            MonthCanChiText = string.Empty;
            YearCanChiText = string.Empty;
            SolarTermText = string.Empty;
            IsSolarTermStartDay = false;
            SolarTermDegreesText = string.Empty;
            return;
        }

        HasLunarDate = true;
        var lunar = LunarCalendar.ToLunar(Date);
        LunarDay = lunar.Day;
        LunarMonth = lunar.Month;
        LunarYear = lunar.Year;
        IsLeapMonth = lunar.IsLeapMonth;
        IsFirstLunarDay = lunar.Day == 1;

        var culture = _localizationService.CurrentCulture;
        bool isEnglish = !culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase);

        // Lunar Day Number
        LunarDayNumberText = lunar.Day.ToString(CultureInfo.InvariantCulture);

        // Lunar Month
        var leapSuffix = lunar.IsLeapMonth
            ? (isEnglish ? " (Leap)" : " (Nhuận)")
            : string.Empty;

        LunarMonthText = isEnglish
            ? $"Month {lunar.Month}{leapSuffix}"
            : $"Tháng {lunar.Month}{leapSuffix}";

        // Can Chi calculations (FR-UI-3, FR-I18N-5, ADR-0008)
        var dayCanChi = LunarCalendar.GetDayCanChi(Date);
        var monthCanChi = LunarCalendar.GetMonthCanChi(lunar);
        var yearCanChi = LunarCalendar.GetYearCanChi(lunar.Year);

        DayCanChiText = LunarFormattingHelper.FormatCanChi(dayCanChi, _localizationService, isEnglish);
        MonthCanChiText = LunarFormattingHelper.FormatCanChi(monthCanChi, _localizationService, isEnglish);
        YearCanChiText = LunarFormattingHelper.FormatYearCanChi(yearCanChi, _localizationService, isEnglish);

        // Solar Term (Tiết khí)
        var tietKhi = LunarCalendar.GetTietKhi(Date);
        SolarTermText = LunarFormattingHelper.FormatSolarTerm(tietKhi, _localizationService, isEnglish);
        IsSolarTermStartDay = tietKhi.IsStartDay;
        SolarTermDegreesText = $"{tietKhi.LongitudeDegrees}°";
    }

    private async Task LoadEventsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var offset = TimeSpan.FromHours(7);
        var startUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var endUtc = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();

        IReadOnlyList<CalendarInfo> calendars;
        IReadOnlyList<CalendarEvent> dayEvents;

        try
        {
            calendars = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken);
            dayEvents = await _eventStore.GetEventsAsync(startUtc, endUtc, cancellationToken: cancellationToken);
        }
        catch
        {
            calendars = Array.Empty<CalendarInfo>();
            dayEvents = Array.Empty<CalendarEvent>();
        }

        var calendarLookup = calendars.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        var allDayLabel = _localizationService["calendar.all_day"];

        Events.Clear();

        var sorted = dayEvents
            .OrderBy(e => e.AllDay ? 0 : 1)
            .ThenBy(e => e.StartUtc)
            .ToList();

        foreach (var ev in sorted)
        {
            var calInfo = calendarLookup.TryGetValue(ev.CalendarId, out var cal) ? cal : null;
            var color = calInfo?.Color ?? "#2F6BFF";
            var calName = calInfo?.Summary ?? string.Empty;

            string timeDisplay = ev.AllDay
                ? allDayLabel
                : $"{ev.StartUtc.ToOffset(offset):HH:mm} – {ev.EndUtc.ToOffset(offset):HH:mm}";

            Events.Add(new AgendaEventItemViewModel(ev, color, calName, timeDisplay));
        }

        HasEvents = Events.Count > 0;
    }

    [RelayCommand]
    private void AddEvent()
    {
        AddEventRequested?.Invoke(this, Date);
    }

    [RelayCommand]
    private void AddLunarRule()
    {
        AddLunarRuleRequested?.Invoke(this, Date);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = LoadDateAsync(Date);
    }

    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLocalizationChanged;
    }
}
