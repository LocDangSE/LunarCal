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
/// View model for the First Run Experience wizard (Figma S14, FR-AUTH-1, FR-I18N-1, FR-LUNAR-6).
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IDisplayPreferences _preferences;
    private readonly ILocalizationService _localization;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly IAccountStore _accountStore;
    private readonly ISyncScheduler? _syncScheduler;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isVietnamese = true;

    [ObservableProperty]
    private bool _isEnglish;

    [ObservableProperty]
    private bool _isLunarEnabled = true;

    [ObservableProperty]
    private bool _isAuthorizing;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Raised when the first run wizard is finished or dismissed.
    /// </summary>
    public event EventHandler? Finished;

    public FirstRunViewModel(
        ISettingsStore settingsStore,
        IDisplayPreferences preferences,
        ILocalizationService localization,
        IGoogleOAuthClient oauthClient,
        IAccountStore accountStore,
        ISyncScheduler? syncScheduler = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _syncScheduler = syncScheduler;

        _isVietnamese = _localization.CurrentCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        _isEnglish = !_isVietnamese;
        _isLunarEnabled = _preferences.LunarEnabled;
    }

    [RelayCommand]
    public void SelectLanguage(string lang)
    {
        if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
        {
            IsEnglish = true;
            IsVietnamese = false;
            _localization.SetCulture(new CultureInfo("en-US"));
        }
        else
        {
            IsVietnamese = true;
            IsEnglish = false;
            _localization.SetCulture(new CultureInfo("vi-VN"));
        }
    }

    [RelayCommand]
    public async Task ContinueStep1Async(CancellationToken cancellationToken = default)
    {
        var lang = IsVietnamese ? "vi-VN" : "en-US";
        await _settingsStore.SetStringAsync(SettingKeys.UiLanguage, lang, cancellationToken).ConfigureAwait(true);
        await _preferences.SetLunarEnabledAsync(IsLunarEnabled, cancellationToken).ConfigureAwait(true);

        CurrentStep = 2;
    }

    [RelayCommand]
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        if (IsAuthorizing)
        {
            return;
        }

        IsAuthorizing = true;
        ErrorMessage = null;

        try
        {
            var authResult = await _oauthClient.AuthorizeAsync(cancellationToken).ConfigureAwait(true);
            if (authResult.Success && authResult.UserInfo is not null)
            {
                var info = authResult.UserInfo;
                var account = new AccountInfo
                {
                    Id = info.Id,
                    Email = info.Email,
                    DisplayName = info.DisplayName
                };

                await _accountStore.SaveAccountAsync(account, cancellationToken).ConfigureAwait(true);
                await _settingsStore.SetStringAsync(SettingKeys.FirstRunCompleted, "true", cancellationToken).ConfigureAwait(true);

                if (_syncScheduler is not null)
                {
                    _ = _syncScheduler.TriggerManualSyncAsync(cancellationToken);
                }

                Finished?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = authResult.Error == "timeout" || authResult.Error == "cancelled"
                    ? _localization["settings.account.auth_cancelled"]
                    : _localization.Format("settings.account.auth_failed", authResult.ErrorDescription ?? authResult.Error ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("settings.account.auth_failed", ex.Message);
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    [RelayCommand]
    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        await _settingsStore.SetStringAsync(SettingKeys.FirstRunCompleted, "true", cancellationToken).ConfigureAwait(true);
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
