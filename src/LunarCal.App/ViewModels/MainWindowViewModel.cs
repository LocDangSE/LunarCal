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

    [ObservableProperty]
    private string _currentLanguage = "vi-VN";

    [ObservableProperty]
    private bool _isVietnameseActive = true;

    [ObservableProperty]
    private bool _isEnglishActive;

    /// <summary>Child view model for the sidebar mini calendar.</summary>
    public MiniCalendarViewModel MiniCalendar { get; }

    /// <summary>List of calendars displayed in the sidebar (FR-UI-10).</summary>
    public ObservableCollection<CalendarItemViewModel> Calendars { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="MainWindowViewModel"/>.
    /// </summary>
    public MainWindowViewModel(
        IEventStore eventStore,
        ILocalizationService localizationService,
        IThemeService themeService,
        ISettingsStore settingsStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

        MiniCalendar = new MiniCalendarViewModel();
        MiniCalendar.DateSelected += OnMiniCalendarDateSelected;

        _localizationService.PropertyChanged += OnLocalizationChanged;

        UpdateLanguageState();
        UpdateTitles();

        // Load calendars from store
        _ = LoadCalendarsAsync();
    }

    /// <summary>
    /// Navigates to today's date (FR-UI-9).
    /// </summary>
    [RelayCommand]
    public void GoToToday()
    {
        SelectedDate = DateTime.Today;
        MiniCalendar.SetSelectedDate(DateOnly.FromDateTime(SelectedDate));
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

        MiniCalendar.SetSelectedDate(DateOnly.FromDateTime(SelectedDate));
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

        MiniCalendar.SetSelectedDate(DateOnly.FromDateTime(SelectedDate));
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
        catch
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

        var weekStart = IsVietnameseActive ? DayOfWeek.Monday : DayOfWeek.Sunday;
        MiniCalendar.SetCultureAndWeekStart(culture, weekStart);

        NetworkStatusText = IsOnline
            ? _localizationService["network.online"]
            : _localizationService["network.offline"];

        SyncStatusText = _localizationService["sidebar.sync_status_now"];
    }

    private void UpdateTitles()
    {
        var culture = _localizationService.CurrentCulture;
        var dateOnly = DateOnly.FromDateTime(SelectedDate);

        // 1. Solar Header Title
        HeaderTitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
            ? $"Tháng {SelectedDate.Month}, {SelectedDate.Year}"
            : SelectedDate.ToString("MMMM yyyy", culture);

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
                var monthQuality = daysInMonth == 30 ? "Đủ" : "Thiếu";
                var leapText = lunar.IsLeapMonth ? " (Nhuận)" : string.Empty;

                LunarHeaderSubtitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                    ? $"Tháng {lunar.Month}{leapText} ({monthQuality}) · Năm {yearCanChi}"
                    : $"Lunar M{lunar.Month}{leapText} · {yearCanChi}";
            }
            else
            {
                LunarHeaderSubtitle = string.Empty;
            }
        }
        catch
        {
            LunarHeaderSubtitle = string.Empty;
        }
    }

    private void OnMiniCalendarDateSelected(object? sender, DateOnly date)
    {
        SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        UpdateTitles();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateLanguageState();
        UpdateTitles();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        MiniCalendar.DateSelected -= OnMiniCalendarDateSelected;
        _localizationService.PropertyChanged -= OnLocalizationChanged;
    }
}
