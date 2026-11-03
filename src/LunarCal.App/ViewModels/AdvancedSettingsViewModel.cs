using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Storage;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for Advanced settings page (Figma S20, FR-AUTH-6, SEC-DATA-3, SEC-LOG-1).
/// </summary>
public sealed partial class AdvancedSettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ILocalDataWipeService? _wipeService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _logsPath = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isWiping;

    public AdvancedSettingsViewModel(
        ISettingsStore settingsStore,
        ILocalizationService localization,
        ILocalDataWipeService? wipeService = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _wipeService = wipeService;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(localAppData, "LunarCal");
        DatabasePath = Path.Combine(baseDir, "lunarcal.db");
        LogsPath = Path.Combine(baseDir, "logs");

        _ = LoadSettingsAsync();
    }

    /// <summary>
    /// Loads saved OAuth override configuration.
    /// </summary>
    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        ClientId = (await _settingsStore.GetStringAsync(SettingKeys.GoogleOAuthClientId, cancellationToken).ConfigureAwait(true)) ?? string.Empty;
        ClientSecret = (await _settingsStore.GetStringAsync(SettingKeys.GoogleOAuthClientSecret, cancellationToken).ConfigureAwait(true)) ?? string.Empty;
    }

    /// <summary>
    /// Saves custom OAuth Client ID and Secret (FR-AUTH-6).
    /// </summary>
    [RelayCommand]
    public async Task SaveOAuthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _settingsStore.SetStringAsync(SettingKeys.GoogleOAuthClientId, ClientId.Trim(), cancellationToken).ConfigureAwait(true);
            await _settingsStore.SetStringAsync(SettingKeys.GoogleOAuthClientSecret, ClientSecret.Trim(), cancellationToken).ConfigureAwait(true);
            StatusMessage = _localization["settings.advanced.oauth_saved"];
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = null;
        }
    }

    /// <summary>
    /// Executes full local data wipe (SEC-DATA-3, P3-04).
    /// </summary>
    [RelayCommand]
    public async Task WipeAllDataAsync(CancellationToken cancellationToken = default)
    {
        if (_wipeService is null)
        {
            return;
        }

        IsWiping = true;
        StatusMessage = null;
        ErrorMessage = null;

        try
        {
            await _wipeService.WipeAllDataAsync(cancellationToken).ConfigureAwait(true);
            StatusMessage = _localization["settings.advanced.wipe_button"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsWiping = false;
        }
    }

    /// <summary>
    /// Opens the activity logs folder in Windows File Explorer (SEC-LOG-1).
    /// </summary>
    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            if (Directory.Exists(LogsPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LogsPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
