using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Google.OAuth;

namespace LunarCal.App.ViewModels;

/// <summary>
/// First run wizard (Figma S14 39:280, 39:336; FR-AUTH-1, FR-I18N-1, FR-LUNAR-6). Step 1 picks the UI language
/// (with that language's week start and clock defaults) and the lunar toggle; step 2 connects Google Calendar or
/// skips to a lunar-only calendar. Step 3 (Gemini API key, optional) is drawn in the stepper but belongs to Phase 4
/// (P4-00, P4-03), so "Tiếp tục" on step 2 finishes the wizard for now.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>Steps shown in the stepper (S14), including the Phase 4 Gemini step.</summary>
    public const int StepCount = 3;

    private readonly ISettingsStore _settingsStore;
    private readonly IDisplayPreferences _preferences;
    private readonly ILocalizationService _localization;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly IAccountStore _accountStore;
    private readonly ISyncScheduler? _syncScheduler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
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
    [NotifyCanExecuteChangedFor(nameof(FinishCommand))]
    private bool _isSignedIn;

    [ObservableProperty]
    private string? _signedInText;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Raised when the first run wizard is finished or dismissed.</summary>
    public event EventHandler? Finished;

    /// <summary>Creates the view model.</summary>
    public FirstRunViewModel(
        ISettingsStore settingsStore,
        IDisplayPreferences preferences,
        ILocalizationService localization,
        IGoogleOAuthClient oauthClient,
        IAccountStore accountStore,
        ISyncScheduler? syncScheduler = null,
        GoogleOAuthOptions? oauthOptions = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _syncScheduler = syncScheduler;
        IsTestingMode = oauthOptions?.IsTestingConsentMode ?? false;

        _isVietnamese = _localization.CurrentCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        _isEnglish = !_isVietnamese;
        _isLunarEnabled = _preferences.LunarEnabled;
        _localization.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(VietnameseDescription));
            OnPropertyChanged(nameof(EnglishDescription));
        };
    }

    /// <summary>"Bước 1 / 3".</summary>
    public string StepLabel => _localization.Format("firstrun.step_of", CurrentStep, StepCount);

    /// <summary>"vi-VN · tuần bắt đầu Thứ 2 · 24 giờ · 22/10/2026": the defaults that step 1 applies for Vietnamese.</summary>
    public string VietnameseDescription => _localization.Format(
        "firstrun.lang_vi_desc", DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN")));

    /// <summary>"en-US · week starts Sunday · 12-hour · Oct 22, 2026": the defaults that step 1 applies for English.</summary>
    public string EnglishDescription => _localization.Format(
        "firstrun.lang_en_desc", DateTime.Today.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")));

    /// <summary>Consent screen in Testing: Google shows "unverified app" (ADR-0026), so S14 warns about it.</summary>
    public bool IsTestingMode { get; }

    /// <summary>Selects the UI language and switches the wizard to it immediately.</summary>
    [RelayCommand]
    public void SelectLanguage(string lang)
    {
        var english = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        IsEnglish = english;
        IsVietnamese = !english;
        _localization.SetCulture(new CultureInfo(english ? "en-US" : "vi-VN"));
    }

    /// <summary>
    /// Step 1 "Tiếp tục": stores the language with the defaults its card describes (week start, 12/24-hour clock;
    /// FR-I18N-1, FR-UI-7) and the lunar toggle (FR-LUNAR-6).
    /// </summary>
    [RelayCommand]
    public async Task ContinueStep1Async(CancellationToken cancellationToken = default)
    {
        var lang = IsVietnamese ? "vi-VN" : "en-US";
        await _settingsStore.SetStringAsync(SettingKeys.UiLanguage, lang, cancellationToken).ConfigureAwait(true);
        await _preferences.SetWeekStartAsync(AppSettingDefaults.ResolveDefaultWeekStart(lang), cancellationToken).ConfigureAwait(true);
        await _preferences.SetTimeFormatAsync(AppSettingDefaults.ResolveDefaultTimeFormat(lang), cancellationToken).ConfigureAwait(true);
        await _preferences.SetLunarEnabledAsync(IsLunarEnabled, cancellationToken).ConfigureAwait(true);

        CurrentStep = 2;
    }

    /// <summary>Step 2 "Quay lại".</summary>
    [RelayCommand]
    public void Back()
    {
        SignInCancelCommand.Execute(null);
        CurrentStep = 1;
    }

    /// <summary>
    /// "Đăng nhập bằng Google" (FR-AUTH-1): system browser + loopback; "Huỷ" cancels the wait. On success the
    /// account is saved and the first sync starts; the user then presses "Tiếp tục".
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
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
                await _accountStore.SaveAccountAsync(
                    new AccountInfo { Id = info.Id, Email = info.Email, DisplayName = info.DisplayName },
                    cancellationToken).ConfigureAwait(true);

                IsSignedIn = true;
                SignedInText = _localization.Format("firstrun.signed_in", info.Email);

                if (_syncScheduler is not null)
                {
                    _ = _syncScheduler.TriggerManualSyncAsync(CancellationToken.None);
                }
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                ErrorMessage = authResult.Error is "timeout" or "cancelled"
                    ? _localization["settings.account.auth_cancelled"]
                    : _localization.Format("settings.account.auth_failed", authResult.ErrorDescription ?? authResult.Error ?? "Unknown");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // "Huỷ": the user stopped waiting; nothing to report.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = _localization.Format("settings.account.auth_failed", ex.Message);
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    /// <summary>Step 2 "Tiếp tục" after signing in: the wizard is done.</summary>
    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        await CompleteAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>"Để sau, chỉ xem âm lịch": finish without a Google account.</summary>
    [RelayCommand]
    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        SignInCancelCommand.Execute(null);
        await CompleteAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _settingsStore.SetStringAsync(SettingKeys.FirstRunCompleted, "true", cancellationToken).ConfigureAwait(true);
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
