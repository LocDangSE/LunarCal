using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Root view model for the shell window (MainWindow, Figma 01).
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly ISettingsStore _settingsStore;
    private readonly IDisplayPreferences _preferences;
    private readonly IMutationService? _mutationService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ISyncScheduler? _syncScheduler;
    private readonly IAccountStore? _accountStore;
    private readonly IGoogleOAuthClient? _oauthClient;

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

    /// <summary>Second line of the sidebar sync card (Figma 1:195): "2 phút trước · Hàng chờ AI: 0".</summary>
    public string SyncDetailText => _localizationService.Format("format.dot_pair", SyncStatusText, PendingAiCommandsText);

    /// <summary>Initials shown in the avatar (Figma 1:227).</summary>
    [ObservableProperty]
    private string _userInitials = InitialsOf(Environment.UserName);

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isOfflineBannerVisible;

    [ObservableProperty]
    private bool _isReauthBannerVisible;

    [ObservableProperty]
    private string _accountEmail = string.Empty;

    [ObservableProperty]
    private string _accountDisplayName = string.Empty;

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

    /// <summary>Raised when any view or command requests the event editor dialog (Figma S10, FR-UI-4).</summary>
    public event EventHandler<EventEditorRequest>? EventEditorRequested;

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
        IDisplayPreferences preferences,
        IMutationService? mutationService = null,
        INetworkStatusService? networkStatusService = null,
        ISyncScheduler? syncScheduler = null,
        IAccountStore? accountStore = null,
        IGoogleOAuthClient? oauthClient = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _mutationService = mutationService;
        _networkStatusService = networkStatusService;
        _syncScheduler = syncScheduler;
        _accountStore = accountStore;
        _oauthClient = oauthClient;

        _isLunarVisible = _preferences.LunarEnabled;

        MiniCalendar = new MiniCalendarViewModel(_localizationService);
        MiniCalendar.DateSelected += OnMiniCalendarDateSelected;

        MonthView = new MonthViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        MonthView.DaySelected += OnMonthViewDaySelected;
        MonthView.EventEditorRequested += OnChildEventEditorRequested;
        MonthView.SetLunarVisibility(IsLunarVisible);
        _ = MonthView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        WeekView = new WeekViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        WeekView.EventEditorRequested += OnChildEventEditorRequested;
        WeekView.SetLunarVisibility(IsLunarVisible);
        _ = WeekView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        DayView = new DayViewModel(_eventStore, _localizationService, _preferences, _mutationService);
        DayView.EventEditorRequested += OnChildEventEditorRequested;
        DayView.SetLunarVisibility(IsLunarVisible);
        _ = DayView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        AgendaView = new AgendaViewModel(_eventStore, _localizationService, _preferences);
        AgendaView.SetLunarVisibility(IsLunarVisible);
        _ = AgendaView.SetDisplayDateAsync(DateOnly.FromDateTime(SelectedDate));

        if (_networkStatusService is not null)
        {
            _isOnline = _networkStatusService.IsOnline;
            _networkStatusService.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        _networkStatusText = _localizationService[IsOnline ? "network.online" : "network.offline"];
        _isOfflineBannerVisible = !IsOnline;

        if (_syncScheduler is not null)
        {
            _syncScheduler.SyncStarted += OnSyncSchedulerStarted;
            _syncScheduler.SyncCompleted += OnSyncSchedulerCompleted;
            UpdateSyncStatusText(_syncScheduler.LastSyncTime);
        }
        else
        {
            _syncStatusText = _localizationService["sidebar.sync_status_now"];
        }

        _localizationService.PropertyChanged += OnLocalizationChanged;
        _preferences.PropertyChanged += OnPreferencesChanged;

        UpdateLanguageState();
        UpdatePendingAiCommandsText();
        UpdateTitles();

        // Load calendars and account from store
        _ = LoadCalendarsAsync();
        _ = LoadAccountAsync();
    }

    private void OnNetworkAvailabilityChanged(object? sender, bool isOnline)
    {
        IsOnline = isOnline;
        NetworkStatusText = _localizationService[isOnline ? "network.online" : "network.offline"];
        IsOfflineBannerVisible = !isOnline;
    }

    private void OnSyncSchedulerStarted(object? sender, EventArgs e)
    {
        IsSyncing = true;
        SyncStatusText = _localizationService["sidebar.sync_status_syncing"];
    }

    private void OnSyncSchedulerCompleted(object? sender, SyncResult result)
    {
        IsSyncing = false;
        UpdateSyncStatusText(_syncScheduler?.LastSyncTime);

        if (result.Success)
        {
            _ = ReloadActiveViewAsync();
        }
        else
        {
            SyncStatusText = _localizationService["sidebar.sync_status_failed"];
        }
    }

    private void UpdateSyncStatusText(DateTimeOffset? lastSync)
    {
        if (lastSync is null)
        {
            SyncStatusText = _localizationService["sidebar.sync_status_now"];
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - lastSync.Value;
        if (elapsed.TotalMinutes < 1)
        {
            SyncStatusText = _localizationService["time.just_now"];
        }
        else if (elapsed.TotalHours < 1)
        {
            SyncStatusText = _localizationService.Format("time.minutes_ago", (int)elapsed.TotalMinutes);
        }
        else
        {
            SyncStatusText = _localizationService.Format("time.hours_ago", (int)elapsed.TotalHours);
        }
    }

    /// <summary>
    /// Loads the authenticated account details into view model properties.
    /// </summary>
    public async Task LoadAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_accountStore is null)
        {
            return;
        }

        try
        {
            var account = await _accountStore.GetPrimaryAccountAsync(cancellationToken).ConfigureAwait(true);
            if (account is not null)
            {
                AccountEmail = account.Email;
                AccountDisplayName = account.DisplayName ?? account.Email;
                UserInitials = InitialsOf(AccountDisplayName);
            }
        }
        catch
        {
            // Fallback gracefully
        }
    }

    private void OnChildEventEditorRequested(object? sender, EventEditorRequest req)
    {
        EventEditorRequested?.Invoke(this, req);
    }

    partial void OnPendingAiCommandsCountChanged(int value) => UpdatePendingAiCommandsText();

    partial void OnSyncStatusTextChanged(string value) => OnPropertyChanged(nameof(SyncDetailText));

    partial void OnPendingAiCommandsTextChanged(string value) => OnPropertyChanged(nameof(SyncDetailText));

    private static string InitialsOf(string? name)
    {
        var words = (name ?? string.Empty).Split([' ', '.', '_', '-', '@'], StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length > 0 ? initials : "?";
    }

    // The view radios also set ActiveViewMode through their IsChecked binding (UI Automation, keyboard) without
    // running ChangeViewCommand, so the titles must follow the property itself.
    partial void OnActiveViewModeChanged(CalendarViewMode value) => UpdateTitles();

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
        var today = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(today);
        _ = MonthView.SetDisplayDateAsync(today);
        _ = WeekView.SetDisplayDateAsync(today);
        _ = DayView.SetDisplayDateAsync(today);
        _ = AgendaView.SetDisplayDateAsync(today);
        UpdateTitles();
    }

    /// <summary>
    /// Navigates one period forward depending on active view mode (FR-UI-9).
    /// </summary>
    [RelayCommand]
    public void NextPeriod()
    {
        SelectedDate = ActiveViewMode switch
        {
            CalendarViewMode.Day => SelectedDate.AddDays(1),
            CalendarViewMode.Week => SelectedDate.AddDays(7),
            _ => SelectedDate.AddMonths(1)
        };

        var date = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(date);
        _ = MonthView.SetDisplayDateAsync(date);
        _ = WeekView.SetDisplayDateAsync(date);
        _ = DayView.SetDisplayDateAsync(date);
        _ = AgendaView.SetDisplayDateAsync(date);
        UpdateTitles();
    }

    /// <summary>
    /// Navigates one period backward depending on active view mode (FR-UI-9).
    /// </summary>
    [RelayCommand]
    public void PreviousPeriod()
    {
        SelectedDate = ActiveViewMode switch
        {
            CalendarViewMode.Day => SelectedDate.AddDays(-1),
            CalendarViewMode.Week => SelectedDate.AddDays(-7),
            _ => SelectedDate.AddMonths(-1)
        };

        var date = DateOnly.FromDateTime(SelectedDate);
        MiniCalendar.SetSelectedDate(date);
        _ = MonthView.SetDisplayDateAsync(date);
        _ = WeekView.SetDisplayDateAsync(date);
        _ = DayView.SetDisplayDateAsync(date);
        _ = AgendaView.SetDisplayDateAsync(date);
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
    /// Triggers an immediate calendar synchronization cycle (FR-UI-10, FR-SYNC-3).
    /// </summary>
    [RelayCommand]
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_syncScheduler is not null)
        {
            IsSyncing = true;
            SyncStatusText = _localizationService["sidebar.sync_status_syncing"];
            try
            {
                await _syncScheduler.TriggerManualSyncAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                IsSyncing = false;
            }
            await ReloadActiveViewAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            SyncStatusText = _localizationService["sidebar.sync_status_now"];
            UpdatePendingAiCommandsText();
        }
    }

    /// <summary>Dismisses the offline warning banner.</summary>
    [RelayCommand]
    public void DismissOfflineBanner() => IsOfflineBannerVisible = false;

    /// <summary>Dismisses the re-authentication warning banner.</summary>
    [RelayCommand]
    public void DismissReauthBanner() => IsReauthBannerVisible = false;

    /// <summary>Initiates re-authentication when token is expired or invalid (FR-AUTH-3).</summary>
    [RelayCommand]
    public async Task ReauthAsync(CancellationToken cancellationToken = default)
    {
        if (_oauthClient is null)
        {
            OpenSettings();
            return;
        }

        try
        {
            var result = await _oauthClient.AuthorizeAsync(cancellationToken).ConfigureAwait(true);
            if (result.Success && result.UserInfo is not null)
            {
                IsReauthBannerVisible = false;
                if (_accountStore is not null)
                {
                    await _accountStore.SaveAccountAsync(new AccountInfo
                    {
                        Id = result.UserInfo.Id,
                        Email = result.UserInfo.Email,
                        DisplayName = result.UserInfo.DisplayName
                    }, cancellationToken).ConfigureAwait(true);
                }
                await LoadAccountAsync(cancellationToken).ConfigureAwait(true);
                await SyncNowAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch
        {
            OpenSettings();
        }
    }

    /// <summary>
    /// Creates a new calendar event (Figma 01, S10, FR-UI-4).
    /// </summary>
    [RelayCommand]
    public void CreateEvent()
    {
        var date = DateOnly.FromDateTime(SelectedDate);
        var time = new TimeOnly(DateTime.Now.Hour < 23 ? DateTime.Now.Hour + 1 : 9, 0);
        EventEditorRequested?.Invoke(this, new EventEditorRequest(EventEditorMode.Create, Date: date, Time: time));
    }

    /// <summary>
    /// Reloads events across the active view after a mutation occurs.
    /// </summary>
    public async Task ReloadActiveViewAsync(CancellationToken cancellationToken = default)
    {
        var date = DateOnly.FromDateTime(SelectedDate);
        switch (ActiveViewMode)
        {
            case CalendarViewMode.Day:
                await DayView.PopulateDayAsync(date, cancellationToken);
                break;
            case CalendarViewMode.Week:
                await WeekView.PopulateWeekAsync(date, cancellationToken);
                break;
            case CalendarViewMode.Agenda:
                await AgendaView.LoadDataAsync(cancellationToken);
                break;
            default:
                await MonthView.PopulateMonthAsync(date);
                break;
        }
    }

    /// <summary>
    /// Event raised to request the Settings window (Figma 04, S13, S20).
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Opens the application settings window.
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
            var list = await _eventStore.GetCalendarsAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            Calendars.Clear();
            foreach (var cal in list)
            {
                Calendars.Add(new CalendarItemViewModel
                {
                    Id = cal.Id,
                    Summary = cal.Summary,
                    Color = cal.Color ?? CalendarColors.DefaultHex,
                    IsSelected = cal.IsSelected,
                    IsPrimary = cal.IsPrimary,
                    AccountId = cal.AccountId
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

        UpdateSyncStatusText(_syncScheduler?.LastSyncTime);
        UpdatePendingAiCommandsText();
    }

    private void UpdateTitles()
    {
        var dateOnly = DateOnly.FromDateTime(SelectedDate);
        var monthFirst = new DateOnly(dateOnly.Year, dateOnly.Month, 1);
        var monthLast = monthFirst.AddMonths(1).AddDays(-1);
        var weekStart = dateOnly.AddDays(-((7 + (dateOnly.DayOfWeek - _preferences.WeekStart)) % 7));
        var weekEnd = weekStart.AddDays(6);

        // Solar title and lunar subtitle per view (Figma 01, S06, S07, S08; FR-UI-9, FR-I18N-4)
        switch (ActiveViewMode)
        {
            case CalendarViewMode.Day:
                HeaderTitle = CalendarText.DayTitle(_localizationService, dateOnly);
                LunarHeaderSubtitle = CalendarText.LunarDayHeader(_localizationService, dateOnly);
                break;
            case CalendarViewMode.Week:
                HeaderTitle = CalendarText.WeekRange(_localizationService, weekStart, weekEnd);
                LunarHeaderSubtitle = CalendarText.LunarWeekHeader(_localizationService, weekStart, weekEnd);
                break;
            default:
                HeaderTitle = CalendarText.MonthYear(_localizationService, dateOnly);
                LunarHeaderSubtitle = CalendarText.LunarMonthHeader(_localizationService, monthFirst, monthLast);
                break;
        }

        // FR-LUNAR-6: no lunar text when the lunar calendar is off
        if (!IsLunarVisible)
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
        if (_networkStatusService is not null)
        {
            _networkStatusService.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }

        if (_syncScheduler is not null)
        {
            _syncScheduler.SyncStarted -= OnSyncSchedulerStarted;
            _syncScheduler.SyncCompleted -= OnSyncSchedulerCompleted;
        }

        _preferences.PropertyChanged -= OnPreferencesChanged;
        MiniCalendar.DateSelected -= OnMiniCalendarDateSelected;
        MonthView.DaySelected -= OnMonthViewDaySelected;
        MonthView.EventEditorRequested -= OnChildEventEditorRequested;
        WeekView.EventEditorRequested -= OnChildEventEditorRequested;
        DayView.EventEditorRequested -= OnChildEventEditorRequested;
        _localizationService.PropertyChanged -= OnLocalizationChanged;
        MonthView.Dispose();
        WeekView.Dispose();
        DayView.Dispose();
        AgendaView.Dispose();
    }
}
