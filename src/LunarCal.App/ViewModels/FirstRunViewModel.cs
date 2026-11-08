using System.Diagnostics;
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
/// First run wizard (Figma S14 39:280, 39:336, 39:395; FR-AUTH-1, FR-I18N-1, FR-LUNAR-6, FR-BYOK-1..3).
/// Step 1: UI language &amp; defaults; Step 2: Google Calendar connection; Step 3: Optional Gemini API Key.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    public const int StepCount = 3;
    private const string AiStudioUrl = "https://aistudio.google.com/apikey";

    private readonly ISettingsStore _settingsStore;
    private readonly IDisplayPreferences _preferences;
    private readonly ILocalizationService _localization;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly IAccountStore _accountStore;
    private readonly IAccountSessionService _accountSession;
    private readonly IAccountSwitchPrompt? _switchPrompt;
    private readonly ISyncScheduler? _syncScheduler;
    private readonly IAiKeyService? _aiKeyService;
    private readonly IClipboardService? _clipboardService;

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
    [NotifyCanExecuteChangedFor(nameof(ContinueStep2Command))]
    private bool _isSignedIn;

    [ObservableProperty]
    private string? _signedInText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isCheckingAiKey;

    [ObservableProperty]
    private string? _aiErrorMessage;

    [ObservableProperty]
    private bool _isAiOffline;

    /// <summary>Raised when the first run wizard is finished or dismissed.</summary>
    public event EventHandler? Finished;

    public FirstRunViewModel(
        ISettingsStore settingsStore,
        IDisplayPreferences preferences,
        ILocalizationService localization,
        IGoogleOAuthClient oauthClient,
        IAccountStore accountStore,
        IAccountSessionService accountSession,
        ISyncScheduler? syncScheduler = null,
        GoogleOAuthOptions? oauthOptions = null,
        IAccountSwitchPrompt? switchPrompt = null,
        IAiKeyService? aiKeyService = null,
        IClipboardService? clipboardService = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _accountSession = accountSession ?? throw new ArgumentNullException(nameof(accountSession));
        _syncScheduler = syncScheduler;
        _switchPrompt = switchPrompt;
        _aiKeyService = aiKeyService;
        _clipboardService = clipboardService;
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

    /// <summary>Defaults applied for Vietnamese.</summary>
    public string VietnameseDescription => _localization.Format(
        "firstrun.lang_vi_desc", DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN")));

    /// <summary>Defaults applied for English.</summary>
    public string EnglishDescription => _localization.Format(
        "firstrun.lang_en_desc", DateTime.Today.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")));

    public bool IsTestingMode { get; }

    [RelayCommand]
    public void SelectLanguage(string lang)
    {
        var english = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        IsEnglish = english;
        IsVietnamese = !english;
        _localization.SetCulture(new CultureInfo(english ? "en-US" : "vi-VN"));
    }

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

    [RelayCommand]
    public void Back()
    {
        SignInCancelCommand.Execute(null);
        CurrentStep = 1;
    }

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    public void ContinueStep2()
    {
        CurrentStep = 3;
    }

    [RelayCommand]
    public void SkipStep2()
    {
        SignInCancelCommand.Execute(null);
        CurrentStep = 3;
    }

    [RelayCommand]
    public void BackStep3()
    {
        CurrentStep = 2;
    }

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
                var outcome = await _accountSession.ApplySignInAsync(info, AccountSwitchPrompts.ToDelegate(_switchPrompt), cancellationToken).ConfigureAwait(true);
                if (outcome == AccountSignInOutcome.Cancelled)
                {
                    ErrorMessage = _localization["settings.account.switch_cancelled"];
                    return;
                }

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

    /// <summary>Completes the wizard.</summary>
    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        await CompleteAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Completes the wizard without a Google account.</summary>
    [RelayCommand]
    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        SignInCancelCommand.Execute(null);
        await CompleteAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public static void OpenAiStudio()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AiStudioUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    /// <summary>Validates and saves the Gemini key on Step 3, then finishes the wizard (FR-BYOK-1, SEC-KEY-6).</summary>
    public async Task<bool> SaveAiKeyAndCompleteAsync(
        string? rawKey,
        bool saveOffline = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            await CompleteAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (_aiKeyService is null)
        {
            await CompleteAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }

        IsCheckingAiKey = true;
        AiErrorMessage = null;

        try
        {
            var result = await _aiKeyService.ValidateAndSaveKeyAsync(rawKey, saveOffline, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                _clipboardService?.ClearIfMatches(rawKey);
                await CompleteAsync(cancellationToken).ConfigureAwait(true);
                return true;
            }

            AiErrorMessage = result.Status switch
            {
                KeyValidationStatus.InvalidKey => _localization["settings.ai.err_invalid"],
                KeyValidationStatus.LeakedKey => _localization["settings.ai.err_leaked"],
                KeyValidationStatus.FreeTierVerificationRequired => _localization["settings.ai.err_freetier"],
                KeyValidationStatus.QuotaExceeded => _localization["settings.ai.err_quota"],
                KeyValidationStatus.NetworkError => _localization["settings.ai.err_network"],
                _ => _localization.Format("settings.ai.err_unexpected", result.Message ?? "Unknown")
            };

            if (result.Status == KeyValidationStatus.NetworkError)
            {
                IsAiOffline = true;
            }

            return false;
        }
        finally
        {
            IsCheckingAiKey = false;
        }
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _settingsStore.SetStringAsync(SettingKeys.FirstRunCompleted, "true", cancellationToken).ConfigureAwait(true);
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
