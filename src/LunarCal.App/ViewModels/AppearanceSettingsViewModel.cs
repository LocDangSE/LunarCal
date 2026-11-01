using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for the Appearance settings page (Figma S13, FR-UI-7, FR-I18N-4, FR-LUNAR-6).
/// Manages theme switching, first day of week, 12h/24h time format, display timezone, and lunar calendar toggle.
/// </summary>
public sealed partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private bool _isInitializing;

    /// <summary>Raised when the configured first day of week changes.</summary>
    public event EventHandler<DayOfWeek>? WeekStartChanged;

    /// <summary>Raised when the configured time format ("24h" or "12h") changes.</summary>
    public event EventHandler<string>? TimeFormatChanged;

    /// <summary>Raised when the display timezone identifier changes.</summary>
    public event EventHandler<string>? TimezoneChanged;

    /// <summary>Raised when the lunar calendar master toggle changes.</summary>
    public event EventHandler<bool>? LunarEnabledChanged;

    [ObservableProperty]
    private string _selectedTheme = AppSettingDefaults.DefaultTheme;

    [ObservableProperty]
    private DayOfWeek _selectedWeekStart = DayOfWeek.Monday;

    [ObservableProperty]
    private string _selectedTimeFormat = TimeFormattingHelper.Format24Hours;

    [ObservableProperty]
    private string _selectedTimezone = TimeZoneInfo.Local.Id;

    [ObservableProperty]
    private bool _isLunarEnabled = AppSettingDefaults.DefaultLunarEnabled;

    [ObservableProperty]
    private string _systemTimezoneText = string.Empty;

    /// <summary>Available system timezones for selection.</summary>
    public IReadOnlyList<TimeZoneInfo> AvailableTimezones { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AppearanceSettingsViewModel"/>.
    /// </summary>
    public AppearanceSettingsViewModel(
        ISettingsStore settingsStore,
        IThemeService themeService,
        ILocalizationService localizationService)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        try
        {
            AvailableTimezones = TimeZoneInfo.GetSystemTimeZones().ToList();
        }
        catch
        {
            AvailableTimezones = new List<TimeZoneInfo> { TimeZoneInfo.Local, TimeZoneInfo.Utc };
        }

        UpdateSystemTimezoneText();
        _localizationService.CultureChanged += (_, _) => UpdateSystemTimezoneText();
    }

    /// <summary>
    /// Loads settings asynchronously from the store.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isInitializing = true;
        try
        {
            SelectedTheme = await _settingsStore.GetThemeAsync(cancellationToken: cancellationToken);
            SelectedWeekStart = await _settingsStore.GetWeekStartAsync(
                AppSettingDefaults.ResolveDefaultWeekStart(_localizationService.CurrentCulture.Name),
                cancellationToken);
            SelectedTimeFormat = await _settingsStore.GetTimeFormatAsync(
                AppSettingDefaults.ResolveDefaultTimeFormat(_localizationService.CurrentCulture.Name),
                cancellationToken);
            SelectedTimezone = await _settingsStore.GetTimezoneAsync(
                AppSettingDefaults.ResolveDefaultTimezone(),
                cancellationToken);
            IsLunarEnabled = await _settingsStore.GetLunarEnabledAsync(cancellationToken: cancellationToken);

            UpdateSystemTimezoneText();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void UpdateSystemTimezoneText()
    {
        var localZone = TimeZoneInfo.Local;
        SystemTimezoneText = _localizationService.Format(
            "settings.appearance.timezone_system",
            $"{localZone.DisplayName} ({localZone.Id})");
    }

    async partial void OnSelectedThemeChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        _themeService.ApplyTheme(value);
        await _settingsStore.SetThemeAsync(value);
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeHighContrast));
    }

    async partial void OnSelectedWeekStartChanged(DayOfWeek value)
    {
        if (_isInitializing)
        {
            return;
        }

        await _settingsStore.SetWeekStartAsync(value);
        WeekStartChanged?.Invoke(this, value);
        OnPropertyChanged(nameof(IsMondayStart));
        OnPropertyChanged(nameof(IsSundayStart));
    }

    async partial void OnSelectedTimeFormatChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        await _settingsStore.SetTimeFormatAsync(value);
        TimeFormatChanged?.Invoke(this, value);
        OnPropertyChanged(nameof(Is24Hours));
        OnPropertyChanged(nameof(Is12Hours));
    }

    async partial void OnSelectedTimezoneChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        await _settingsStore.SetTimezoneAsync(value);
        TimezoneChanged?.Invoke(this, value);
    }

    async partial void OnIsLunarEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        await _settingsStore.SetLunarEnabledAsync(value);
        LunarEnabledChanged?.Invoke(this, value);
    }

    // Helper properties for direct binding to radio buttons
    public bool IsThemeSystem
    {
        get => string.Equals(SelectedTheme, "System", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTheme = "System";
            }
        }
    }

    public bool IsThemeLight
    {
        get => string.Equals(SelectedTheme, "Light", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTheme = "Light";
            }
        }
    }

    public bool IsThemeDark
    {
        get => string.Equals(SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTheme = "Dark";
            }
        }
    }

    public bool IsThemeHighContrast
    {
        get => string.Equals(SelectedTheme, "HighContrast", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTheme = "HighContrast";
            }
        }
    }

    public bool IsMondayStart
    {
        get => SelectedWeekStart == DayOfWeek.Monday;
        set
        {
            if (value)
            {
                SelectedWeekStart = DayOfWeek.Monday;
            }
        }
    }

    public bool IsSundayStart
    {
        get => SelectedWeekStart == DayOfWeek.Sunday;
        set
        {
            if (value)
            {
                SelectedWeekStart = DayOfWeek.Sunday;
            }
        }
    }

    public bool Is24Hours
    {
        get => string.Equals(SelectedTimeFormat, TimeFormattingHelper.Format24Hours, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTimeFormat = TimeFormattingHelper.Format24Hours;
            }
        }
    }

    public bool Is12Hours
    {
        get => string.Equals(SelectedTimeFormat, TimeFormattingHelper.Format12Hours, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                SelectedTimeFormat = TimeFormattingHelper.Format12Hours;
            }
        }
    }
}
