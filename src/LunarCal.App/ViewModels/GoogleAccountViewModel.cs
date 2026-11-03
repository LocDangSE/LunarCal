using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Google.OAuth;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for Google Account settings page (Figma 04, FR-AUTH-1, FR-AUTH-4, FR-AUTH-6).
/// </summary>
public sealed partial class GoogleAccountViewModel : ObservableObject
{
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly IGoogleSignOutService _signOutService;
    private readonly IAccountStore _accountStore;
    private readonly ISyncScheduler? _syncScheduler;
    private readonly ILocalizationService _localization;
    private readonly GoogleOAuthOptions? _oauthOptions;

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _accountId = string.Empty;

    [ObservableProperty]
    private string _accountEmail = string.Empty;

    [ObservableProperty]
    private string _accountDisplayName = string.Empty;

    [ObservableProperty]
    private string _accountInitials = "?";

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _lastSyncText = string.Empty;

    [ObservableProperty]
    private bool _isTestingMode;

    [ObservableProperty]
    private bool _isAuthorizing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Event raised when the account state changes (sign-in or sign-out).
    /// </summary>
    public event EventHandler? AccountChanged;

    public GoogleAccountViewModel(
        IGoogleOAuthClient oauthClient,
        IGoogleSignOutService signOutService,
        IAccountStore accountStore,
        ILocalizationService localization,
        ISyncScheduler? syncScheduler = null,
        GoogleOAuthOptions? oauthOptions = null)
    {
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _signOutService = signOutService ?? throw new ArgumentNullException(nameof(signOutService));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _syncScheduler = syncScheduler;
        _oauthOptions = oauthOptions;

        _isTestingMode = _oauthOptions?.IsTestingConsentMode ?? false;

        if (_syncScheduler is not null)
        {
            _syncScheduler.SyncStarted += OnSyncStarted;
            _syncScheduler.SyncCompleted += OnSyncCompleted;
        }

        _ = LoadAccountAsync();
    }

    /// <summary>
    /// Loads the stored primary account from the account store.
    /// </summary>
    public async Task LoadAccountAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accountStore.GetPrimaryAccountAsync(cancellationToken).ConfigureAwait(true);
        if (account is not null)
        {
            AccountId = account.Id;
            AccountEmail = account.Email;
            AccountDisplayName = account.DisplayName ?? account.Email;
            AccountInitials = ComputeInitials(AccountDisplayName);
            IsSignedIn = true;
            UpdateLastSyncText(_syncScheduler?.LastSyncTime);
        }
        else
        {
            AccountId = string.Empty;
            AccountEmail = string.Empty;
            AccountDisplayName = string.Empty;
            AccountInitials = "?";
            IsSignedIn = false;
            LastSyncText = _localization["settings.account.never_synced"];
        }
    }

    private void OnSyncStarted(object? sender, EventArgs e)
    {
        IsSyncing = true;
        StatusMessage = _localization["settings.account.syncing"];
    }

    private void OnSyncCompleted(object? sender, SyncResult result)
    {
        IsSyncing = false;
        UpdateLastSyncText(_syncScheduler?.LastSyncTime);
        if (result.Success)
        {
            StatusMessage = _localization["sidebar.sync_title"];
            ErrorMessage = null;
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
            StatusMessage = null;
        }
    }

    /// <summary>
    /// Formats relative sync time string according to current culture.
    /// </summary>
    public static string FormatRelativeSyncTime(DateTimeOffset? lastSync, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        if (lastSync is null)
        {
            return localization["settings.account.never_synced"];
        }

        var elapsed = DateTimeOffset.UtcNow - lastSync.Value;
        string relativeTime;
        if (elapsed.TotalMinutes < 1)
        {
            relativeTime = localization["time.just_now"];
        }
        else if (elapsed.TotalHours < 1)
        {
            relativeTime = localization.Format("time.minutes_ago", (int)elapsed.TotalMinutes);
        }
        else if (elapsed.TotalDays < 1)
        {
            relativeTime = localization.Format("time.hours_ago", (int)elapsed.TotalHours);
        }
        else
        {
            relativeTime = localization.Format("time.days_ago", (int)elapsed.TotalDays);
        }

        return localization.Format("settings.account.last_synced", relativeTime);
    }

    private void UpdateLastSyncText(DateTimeOffset? lastSync)
    {
        LastSyncText = FormatRelativeSyncTime(lastSync, _localization);
    }



    private static string ComputeInitials(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var parts = text.Split([' ', '.', '@', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return initials.Length > 0 ? initials : "?";
    }

    /// <summary>
    /// Executes interactive Google OAuth sign-in flow (FR-AUTH-1).
    /// </summary>
    [RelayCommand]
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        if (IsAuthorizing)
        {
            return;
        }

        IsAuthorizing = true;
        ErrorMessage = null;
        StatusMessage = _localization["settings.account.authorizing"];

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

                AccountId = account.Id;
                AccountEmail = account.Email;
                AccountDisplayName = account.DisplayName ?? account.Email;
                AccountInitials = ComputeInitials(AccountDisplayName);
                IsSignedIn = true;
                StatusMessage = null;

                AccountChanged?.Invoke(this, EventArgs.Empty);

                // Trigger initial sync for new account
                if (_syncScheduler is not null)
                {
                    _ = _syncScheduler.TriggerManualSyncAsync(cancellationToken);
                }
            }
            else
            {
                ErrorMessage = authResult.Error == "timeout" || authResult.Error == "cancelled"
                    ? _localization["settings.account.auth_cancelled"]
                    : _localization.Format("settings.account.auth_failed", authResult.ErrorDescription ?? authResult.Error ?? "Unknown");
                StatusMessage = null;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("settings.account.auth_failed", ex.Message);
            StatusMessage = null;
        }
        finally
        {
            IsAuthorizing = false;
        }
    }

    /// <summary>
    /// Signs out the active account with user confirmation (FR-AUTH-4).
    /// </summary>
    [RelayCommand]
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn || string.IsNullOrWhiteSpace(AccountId))
        {
            return;
        }

        try
        {
            var result = await _signOutService.SignOutAsync(AccountId, confirmed: true, cancellationToken).ConfigureAwait(true);
            if (result.Success)
            {
                await _accountStore.DeleteAccountAsync(AccountId, cancellationToken).ConfigureAwait(true);
                AccountId = string.Empty;
                AccountEmail = string.Empty;
                AccountDisplayName = string.Empty;
                AccountInitials = "?";
                IsSignedIn = false;
                StatusMessage = null;
                ErrorMessage = null;
                LastSyncText = _localization["settings.account.never_synced"];

                AccountChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (result.Error is not null)
            {
                ErrorMessage = result.Error;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Triggers an immediate manual sync pass (FR-UI-10, FR-SYNC-3).
    /// </summary>
    [RelayCommand]
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_syncScheduler is not null)
        {
            IsSyncing = true;
            StatusMessage = _localization["settings.account.syncing"];
            try
            {
                await _syncScheduler.TriggerManualSyncAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                IsSyncing = false;
            }
        }
        else
        {
            UpdateLastSyncText(DateTimeOffset.UtcNow);
        }
    }
}
