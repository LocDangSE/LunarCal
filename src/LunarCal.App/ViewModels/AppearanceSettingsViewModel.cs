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
    private readonly IDisplayPreferences _preferences;
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

    /// <summary>Localized error shown when the last change could not be saved; empty when the last save succeeded.</summary>
    [ObservableProperty]
    private string _saveErrorText = string.Empty;

    /// <summary>Available system timezones for selection.</summary>
    public IReadOnlyList<TimeZoneInfo> AvailableTimezones { get; }

    /// <summary>
    /// Completes when the most recent change has been applied and persisted. Exceptions are never surfaced here;
    /// they are reported through <see cref="SaveErrorText"/>.
    /// </summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of <see cref="AppearanceSettingsViewModel"/>.
    /// Week start, time format, time zone and lunar toggle are applied through <see cref="IDisplayPreferences"/>,
    /// so every open view updates immediately (FR-UI-7, FR-I18N-4, FR-LUNAR-6).
    /// </summary>
    public AppearanceSettingsViewModel(
        ISettingsStore settingsStore,
        IThemeService themeService,
        ILocalizationService localizationService,
        IDisplayPreferences preferences)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        try
        {
            AvailableTimezones = TimeZoneInfo.GetSystemTimeZones().ToList();
        }
        catch (TimeZoneNotFoundException)
        {
            AvailableTimezones = new List<TimeZoneInfo> { TimeZoneInfo.Local, TimeZoneInfo.Utc };
        }

        UpdateSystemTimezoneText();
        _localizationService.CultureChanged += (_, _) => UpdateSystemTimezoneText();
    }

    /// <summary>
    /// Loads the current values (theme from the settings store, the rest from <see cref="IDisplayPreferences"/>).
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _isInitializing = true;
        try
        {
            SelectedTheme = await _settingsStore.GetThemeAsync(cancellationToken: cancellationToken);
            SelectedWeekStart = _preferences.WeekStart;
            SelectedTimeFormat = _preferences.TimeFormat;
            SelectedTimezone = _preferences.TimeZone.Id;
            IsLunarEnabled = _preferences.LunarEnabled;

            UpdateSystemTimezoneText();
        }
        finally
        {
            _isInitializing = false;
        }

        OnPropertyChanged(string.Empty);
    }

    private void UpdateSystemTimezoneText()
    {
        var localZone = TimeZoneInfo.Local;
        SystemTimezoneText = _localizationService.Format(
            "settings.appearance.timezone_system",
            $"{localZone.DisplayName} ({localZone.Id})");
    }

    partial void OnSelectedThemeChanged(string value)
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeHighContrast));
        if (_isInitializing)
        {
            return;
        }

        _themeService.ApplyTheme(value);
        Save(ct => _settingsStore.SetThemeAsync(value, ct).AsTask());
    }

    partial void OnSelectedWeekStartChanged(DayOfWeek value)
    {
        OnPropertyChanged(nameof(IsMondayStart));
        OnPropertyChanged(nameof(IsSundayStart));
        if (_isInitializing)
        {
            return;
        }

        Save(ct => _preferences.SetWeekStartAsync(value, ct), () => WeekStartChanged?.Invoke(this, value));
    }

    partial void OnSelectedTimeFormatChanged(string value)
    {
        OnPropertyChanged(nameof(Is24Hours));
        OnPropertyChanged(nameof(Is12Hours));
        if (_isInitializing)
        {
            return;
        }

        Save(ct => _preferences.SetTimeFormatAsync(value, ct), () => TimeFormatChanged?.Invoke(this, value));
    }

    partial void OnSelectedTimezoneChanged(string value)
    {
        if (_isInitializing || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Save(ct => _preferences.SetTimeZoneAsync(value, ct), () => TimezoneChanged?.Invoke(this, value));
    }

    partial void OnIsLunarEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save(ct => _preferences.SetLunarEnabledAsync(value, ct), () => LunarEnabledChanged?.Invoke(this, value));
    }

    // Chains saves so they are applied in order, and observes every exception (no async void, AGENTS.md §7).
    private void Save(Func<CancellationToken, Task> operation, Action? onSaved = null)
    {
        var previous = PendingSave;
        PendingSave = RunAsync();

        async Task RunAsync()
        {
            await previous.ConfigureAwait(true);
            try
            {
                await operation(CancellationToken.None).ConfigureAwait(true);
                SaveErrorText = string.Empty;
                onSaved?.Invoke();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SaveErrorText = _localizationService["settings.save_error"];
            }
        }
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
