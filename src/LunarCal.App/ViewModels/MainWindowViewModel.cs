using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Lunar;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Root view model for the main application shell window (Figma 01, FR-UI-1, FR-UI-9, FR-UI-10).
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly ISettingsStore _settingsStore;
    private readonly IDisplayPreferences _preferences;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private CalendarViewMode _activeViewMode = CalendarViewMode.Month;

    [ObservableProperty]
    private string _headerTitle = string.Empty;

    [ObservableProperty]
    private string _lunarHeaderSubtitle = string.Empty;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _networkStatusText = string.Empty;

    [ObservableProperty]
    private string _syncStatusText = string.Empty;

    [ObservableProperty]
    private int _pendingAiCommandsCount;

    /// <summary>Localized pending AI command count for the sidebar badge (FR-I18N-3).</summary>
    [ObservableProperty]
    private string _pendingAiCommandsText = string.Empty;

    [ObservableProperty]
    private string _currentLanguage = "vi-VN";

    [ObservableProperty]
    private bool _isVietnameseActive = true;

    [ObservableProperty]
    private bool _isEnglishActive;

    /// <summary>Child view model for the sidebar mini calendar.</summary>
    public MiniCalendarViewModel MiniCalendar { get; }

    /// <summary>Child view model for the main 6×7 month view grid (FR-UI-1, FR-UI-2, FR-LUNAR-6).</summary>
    public MonthViewModel MonthView { get; }

    /// <summary>Child view model for the 7-day week view (Figma S06, FR-UI-1).</summary>
    public WeekViewModel WeekView { get; }

    /// <summary>Child view model for the single day view (Figma S07, FR-UI-1).</summary>
    public DayViewModel DayView { get; }

    /// <summary>Child view model for the agenda schedule view (Figma S08, FR-UI-1).</summary>
    public AgendaViewModel AgendaView { get; }

    /// <summary>List of calendars displayed in the sidebar (FR-UI-10).</summary>
    public ObservableCollection<CalendarItemViewModel> Calendars { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="MainWindowViewModel"/>.
    /// </summary>
    public MainWindowViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        IThemeService themeService,
        ISettingsStore settingsStore,
        IDisplayPreferences preferences)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _isLunarVisible = _preferences.LunarEnabled;

        MiniCalendar = new MiniCalendarViewModel(_localizationService);
        MiniCalendar.DateSelected += OnMiniCalendarDateSelected;

        MonthView = new MonthViewModel(_eventStore, _localizationService, _preferences);
        MonthView.DaySelected += OnMonthViewDaySelected;
        MonthView.SetLunarVisibility(IsLunarVisible);
        _ = MonthView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        WeekView = new WeekViewModel(_eventStore, _localizationService, _preferences);
        WeekView.SetLunarVisibility(IsLunarVisible);
        _ = WeekView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        DayView = new DayViewModel(_eventStore, _localizationService, _preferences);
        DayView.SetLunarVisibility(IsLunarVisible);
        _ = DayView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        AgendaView = new AgendaViewModel(_eventStore, _localizationService, _preferences);
        AgendaView.SetLunarVisibility(IsLunarVisible);
        _ = AgendaView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        _localizationService.PropertyChanged += OnLocalizationChanged;
        _preferences.PropertyChanged += OnPreferencesChanged;

        UpdateLanguageState();
        UpdateTitles();

        // Load calendars from store
        _ = LoadCalendarsAsync();
    }

    partial void OnPendingAiCommandsCountChanged(int value) => UpdatePendingAiCommandsText();

    private void UpdatePendingAiCommandsText() =>
        PendingAiCommandsText = _localizationService.Format("sidebar.pending_ai_commands", PendingAiCommandsCount);

    partial void OnIsLunarVisibleChanged(bool value)
    {
        MonthView.SetLunarVisibility(value);
        WeekView.SetLunarVisibility(value);
        DayView.SetLunarVisibility(value);
        AgendaView.SetLunarVisibility(value);
        UpdateTitles();
    }

    /// <summary>
    /// Navigates to today's date (FR-UI-9).
    /// </summary>
    [RelayCommand]
    public void GoToToday()
    {
        SelectedDate = DateTime.Today;
        var dateOnly = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(dateOnly);
        _ = MonthView.SetDisplayDateAsync(dateOnly);
        _ = WeekView.SetDisplayDateAsync(dateOnly);
        _ = DayView.SetDisplayDateAsync(dateOnly);
        _ = AgendaView.SetDisplayDateAsync(dateOnly);
        UpdateTitles();
    }

    /// <summary>
    /// Navigates back by one period (month, week, or day depending on active view).
    /// </summary>
    [RelayCommand]
    public void PreviousPeriod()
    {
        SelectedDate = ActiveViewMode switch
        {
            CalendarViewMode.Day => SelectedDate.AddDays(-1),
            CalendarViewMode.Week => SelectedDate.AddDays(-7),
            _ => SelectedDate.AddMonths(-1),
        };

        var dateOnly = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(dateOnly);
        _ = MonthView.SetDisplayDateAsync(dateOnly);
        _ = WeekView.SetDisplayDateAsync(dateOnly);
        _ = DayView.SetDisplayDateAsync(dateOnly);
        _ = AgendaView.SetDisplayDateAsync(dateOnly);
        UpdateTitles();
    }

    /// <summary>
    /// Navigates forward by one period (month, week, or day depending on active view).
    /// </summary>
    [RelayCommand]
    public void NextPeriod()
    {
        SelectedDate = ActiveViewMode switch
        {
            CalendarViewMode.Day => SelectedDate.AddDays(1),
            CalendarViewMode.Week => SelectedDate.AddDays(7),
            _ => SelectedDate.AddMonths(1),
        };

        var dateOnly = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(dateOnly);
        _ = MonthView.SetDisplayDateAsync(dateOnly);
        _ = WeekView.SetDisplayDateAsync(dateOnly);
        _ = DayView.SetDisplayDateAsync(dateOnly);
        _ = AgendaView.SetDisplayDateAsync(dateOnly);
        UpdateTitles();
    }

    /// <summary>
    /// Changes the active calendar view mode (Day, Week, Month, Agenda).
    /// </summary>
    [RelayCommand]
    public void ChangeView(CalendarViewMode mode)
    {
        ActiveViewMode = mode;
        UpdateTitles();
    }

    /// <summary>
    /// Switches the application UI culture between vi-VN and en-US (FR-UI-9, FR-I18N-1).
    /// </summary>
    [RelayCommand]
    public async Task SwitchLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        var culture = new CultureInfo(languageCode.Trim());
        _localizationService.SetCulture(culture);

        // Update settings store
        await _settingsStore.SetStringAsync(SettingKeys.UiLanguage, culture.Name).ConfigureAwait(true);

        UpdateLanguageState();
        UpdateTitles();
    }

    /// <summary>
    /// Toggles visibility of events for a calendar collection.
    /// </summary>
    [RelayCommand]
    public void ToggleCalendar(CalendarItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        OnPropertyChanged(nameof(Calendars));
    }

    /// <summary>
    /// Triggers an immediate calendar synchronization cycle.
    /// </summary>
    [RelayCommand]
    public void SyncNow()
    {
        SyncStatusText = _localizationService["sidebar.sync_status_now"];
        UpdatePendingAiCommandsText();
    }

    /// <summary>
    /// Creates a new calendar event.
    /// </summary>
    [RelayCommand]
    public void CreateEvent()
    {
        // Event creation dialog is wired in P3-14 with SelectedDate context
        _ = SelectedDate;
    }

    /// <summary>Raised when the user asks to open Settings (Figma S13). The view opens the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Opens the Settings screen (Cài đặt &gt; Giao diện, Figma S13, FR-UI-7).
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Loads calendar collections from the event store into the sidebar list.
    /// </summary>
    public async Task LoadCalendarsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken);
            Calendars.Clear();
            foreach (var cal in list)
            {
                Calendars.Add(new CalendarItemViewModel
                {
                    Id = cal.Id,
                    Summary = cal.Summary,
                    Color = cal.Color ?? "#4285F4",
                    IsSelected = cal.IsSelected,
                    IsPrimary = cal.IsPrimary,
                    AccountId = cal.AccountId,
                });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Fallback gracefully
        }
    }

    private void UpdateLanguageState()
    {
        var culture = _localizationService.CurrentCulture;
        CurrentLanguage = culture.Name;
        IsVietnameseActive = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        IsEnglishActive = !IsVietnameseActive;

        // Week start comes from the user's setting (FR-UI-7), not from the UI language.
        var weekStart = _preferences.WeekStart;
        MiniCalendar.SetCultureAndWeekStart(culture, weekStart);
        MonthView.SetWeekStart(weekStart);
        WeekView.SetCultureAndWeekStart(culture, weekStart);
        DayView.SetCulture(culture);
        AgendaView.SetCulture(culture);

        NetworkStatusText = IsOnline
            ? _localizationService["network.online"]
            : _localizationService["network.offline"];

        SyncStatusText = _localizationService["sidebar.sync_status_now"];
    }

    private void UpdateTitles()
    {
        var culture = _localizationService.CurrentCulture;
        var dateOnly = DateOnly.FromDateTime(SelectedDate);

        // 1. Solar Header Title (FR-I18N-4: wording and order come from resx)
        switch (ActiveViewMode)
        {
            case CalendarViewMode.Day:
                HeaderTitle = CalendarText.DayFull(_localizationService, dateOnly);
                break;
            case CalendarViewMode.Week:
                int diff = (7 + (dateOnly.DayOfWeek - _preferences.WeekStart)) % 7;
                var weekStart = dateOnly.AddDays(-diff);
                HeaderTitle = CalendarText.WeekRange(_localizationService, weekStart, weekStart.AddDays(6));
                break;
            default:
                HeaderTitle = CalendarText.MonthYear(_localizationService, dateOnly);
                break;
        }

        // 2. Lunar Header Subtitle (FR-UI-9, FR-LUNAR-1, FR-LUNAR-6)
        if (!IsLunarVisible)
        {
            LunarHeaderSubtitle = string.Empty;
            return;
        }

        try
        {
            if (dateOnly >= LunarCalendar.MinSolarDate && dateOnly <= LunarCalendar.MaxSolarDate)
            {
                var lunar = LunarCalendar.ToLunar(dateOnly);
                var yearCanChi = LunarCalendar.GetYearCanChi(lunar.Year);
                var daysInMonth = LunarCalendar.GetDaysInMonth(lunar.Year, lunar.Month, lunar.IsLeapMonth);
                var monthQuality = _localizationService[daysInMonth == 30 ? "lunar.month_full" : "lunar.month_short"];
                var month = lunar.IsLeapMonth
                    ? string.Create(culture, $"{lunar.Month} {_localizationService["lunar.leap"]}")
                    : lunar.Month.ToString(culture);

                if (ActiveViewMode == CalendarViewMode.Day)
                {
                    var dayCanChi = LunarCalendar.GetDayCanChi(dateOnly);
                    var tietKhi = LunarCalendar.GetTietKhi(dateOnly);
                    LunarHeaderSubtitle = _localizationService.Format(
                        "lunar.header_day", lunar.Day, month, dayCanChi, tietKhi.VietnameseName);
                }
                else
                {
                    LunarHeaderSubtitle = _localizationService.Format(
                        "lunar.header_month", month, monthQuality, yearCanChi);
                }
            }
            else
            {
                LunarHeaderSubtitle = string.Empty;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LunarHeaderSubtitle = string.Empty;
        }
    }

    private void OnMiniCalendarDateSelected(object? sender, DateOnly date)
    {
        SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        _ = MonthView.SetDisplayDateAsync(date);
        _ = WeekView.SetDisplayDateAsync(date);
        _ = DayView.SetDisplayDateAsync(date);
        _ = AgendaView.SetDisplayDateAsync(date);
        UpdateTitles();
    }

    private void OnMonthViewDaySelected(object? sender, DateOnly date)
    {
        SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        MiniCalendar.SetSelectedDate(date);
        _ = WeekView.SetDisplayDateAsync(date);
        _ = DayView.SetDisplayDateAsync(date);
        _ = AgendaView.SetDisplayDateAsync(date);
        UpdateTitles();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateLanguageState();
        UpdateTitles();
    }

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var date = DateOnly.FromDateTime(SelectedDate);
        switch (e.PropertyName)
        {
            case nameof(IDisplayPreferences.WeekStart):
                MiniCalendar.SetCultureAndWeekStart(_localizationService.CurrentCulture, _preferences.WeekStart);
                MonthView.SetWeekStart(_preferences.WeekStart);
                WeekView.SetWeekStart(_preferences.WeekStart);
                UpdateTitles();
                break;
            case nameof(IDisplayPreferences.LunarEnabled):
                IsLunarVisible = _preferences.LunarEnabled;
                break;
            case nameof(IDisplayPreferences.TimeZone):
            case nameof(IDisplayPreferences.TimeFormat):
                _ = MonthView.SetDisplayDateAsync(date);
                _ = WeekView.SetDisplayDateAsync(date);
                _ = DayView.SetDisplayDateAsync(date);
                _ = AgendaView.SetDisplayDateAsync(date);
                break;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _preferences.PropertyChanged -= OnPreferencesChanged;
        MiniCalendar.DateSelected -= OnMiniCalendarDateSelected;
        MonthView.DaySelected -= OnMonthViewDaySelected;
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        MonthView.Dispose();
        WeekView.Dispose();
        DayView.Dispose();
        AgendaView.Dispose();
    }
}
