using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>Settings window (Figma 04, S13, S20): navigation, the "Chung" section and the child page view models.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ILocalizationService? _localization;
    private readonly ISettingsStore? _settingsStore;

    [ObservableProperty]
    private SettingsTab _activeTab = SettingsTab.General;

    /// <summary>Raised by "← Quay lại lịch".</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Theme, week start, time format, time zone, lunar toggle (S13, Figma 04 "Chung").</summary>
    public AppearanceSettingsViewModel Appearance { get; }

    /// <summary>Google account and sync (Figma 04).</summary>
    public GoogleAccountViewModel Account { get; }

    /// <summary>OAuth client, local data, logs (S20).</summary>
    public AdvancedSettingsViewModel Advanced { get; }

    /// <summary>Creates the settings view model.</summary>
    public SettingsViewModel(
        AppearanceSettingsViewModel appearance,
        GoogleAccountViewModel account,
        AdvancedSettingsViewModel advanced,
        ILocalizationService? localization = null,
        ISettingsStore? settingsStore = null)
    {
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Advanced = advanced ?? throw new ArgumentNullException(nameof(advanced));
        _localization = localization;
        _settingsStore = settingsStore;
    }

    /// <summary>Whether the UI language is Vietnamese (segmented "Tiếng Việt | English", FR-I18N-1).</summary>
    public bool IsVietnamese
    {
        get => _localization?.CurrentCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ?? true;
        set
        {
            if (value)
            {
                _ = SetLanguageAsync("vi-VN");
            }
        }
    }

    /// <summary>Whether the UI language is English.</summary>
    public bool IsEnglish
    {
        get => !IsVietnamese;
        set
        {
            if (value)
            {
                _ = SetLanguageAsync("en-US");
            }
        }
    }

    /// <summary>Switches the UI language and stores it (FR-I18N-1); calendar data is unchanged.</summary>
    public async Task SetLanguageAsync(string cultureName, CancellationToken cancellationToken = default)
    {
        if (_localization is null)
        {
            return;
        }

        _localization.SetCulture(new CultureInfo(cultureName));
        if (_settingsStore is not null)
        {
            await _settingsStore.SetStringAsync(SettingKeys.UiLanguage, _localization.CurrentCulture.Name, cancellationToken).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(IsVietnamese));
        OnPropertyChanged(nameof(IsEnglish));
    }

    /// <summary>Selects a navigation entry.</summary>
    [RelayCommand]
    public void SelectTab(SettingsTab tab) => ActiveTab = tab;

    /// <summary>"← Quay lại lịch".</summary>
    [RelayCommand]
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
