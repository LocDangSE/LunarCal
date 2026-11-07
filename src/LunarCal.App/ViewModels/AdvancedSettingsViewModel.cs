using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Settings > Advanced (Figma S20): own OAuth client (FR-AUTH-6), local data location, size and wipe (SEC-DATA-3),
/// log level and folder (SEC-LOG-1). Every destructive action asks for confirmation first.
/// </summary>
public sealed partial class AdvancedSettingsViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly ILocalDataWipeService? _wipeService;
    private readonly IOAuthClientOverrideService? _oauthOverride;
    private readonly IConfirmationService? _confirmation;
    private readonly IGoogleSignOutService? _signOut;
    private readonly IAccountStore? _accounts;
    private readonly LogLevelService? _logLevel;
    private bool _loading;

    /// <summary>"Dùng OAuth client riêng" toggle; shows the client form.</summary>
    [ObservableProperty]
    private bool _useCustomClient;

    [ObservableProperty]
    private string _clientId = string.Empty;

    /// <summary>New client secret typed by the user; empty keeps the stored one. Never loaded back from storage.</summary>
    [ObservableProperty]
    private string _clientSecret = string.Empty;

    /// <summary>Whether a client secret is already stored (shown as dots).</summary>
    [ObservableProperty]
    private bool _hasStoredSecret;

    [ObservableProperty]
    private bool _isDebugLogging;

    [ObservableProperty]
    private string _storageSizeText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isWiping;

    /// <summary>Raised after local data was wiped; the shell exits because its caches are gone.</summary>
    public event EventHandler? WipeCompleted;

    /// <summary>Creates the view model.</summary>
    public AdvancedSettingsViewModel(
        ILocalizationService localization,
        ILocalDataWipeService? wipeService = null,
        IOAuthClientOverrideService? oauthOverride = null,
        IConfirmationService? confirmation = null,
        IGoogleSignOutService? signOut = null,
        IAccountStore? accounts = null,
        LogLevelService? logLevel = null)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _wipeService = wipeService;
        _oauthOverride = oauthOverride;
        _confirmation = confirmation;
        _signOut = signOut;
        _accounts = accounts;
        _logLevel = logLevel;

        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarCal");
        DataDirectory = baseDir;
        LogsPath = Path.Combine(baseDir, "logs");
        Load();
    }

    /// <summary>%LOCALAPPDATA%\LunarCal.</summary>
    public string DataDirectory { get; }

    /// <summary>Log folder.</summary>
    public string LogsPath { get; }

    /// <summary>Text shown for the data location row.</summary>
    public string DataLocationText => _localization.Format("settings.advanced.data_location_desc", DataDirectory);

    private void Load()
    {
        _loading = true;
        UseCustomClient = _oauthOverride?.CustomClientId is not null;
        ClientId = _oauthOverride?.CustomClientId ?? string.Empty;
        HasStoredSecret = _oauthOverride?.HasCustomClientSecret ?? false;
        IsDebugLogging = _logLevel?.IsDebug ?? false;
        StorageSizeText = FormatSize(DirectorySize(DataDirectory) - DirectorySize(LogsPath));
        _loading = false;
    }

    partial void OnIsDebugLoggingChanged(bool value)
    {
        if (!_loading && _logLevel is not null)
        {
            _ = _logLevel.SetDebugAsync(value);
        }
    }

    partial void OnUseCustomClientChanged(bool value)
    {
        // Turning the toggle off while a custom client is active is the same as "Dùng client mặc định".
        if (!_loading && !value && _oauthOverride?.CustomClientId is not null)
        {
            _ = UseDefaultClientAsync();
        }
    }

    /// <summary>
    /// "Lưu và đăng nhập lại": changing the client signs every account out (tokens belong to the old client), so the
    /// user confirms first; then the client is stored (secret in DPAPI) and used for the next sign-in.
    /// </summary>
    [RelayCommand]
    public async Task SaveOAuthAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (_oauthOverride is null || string.IsNullOrWhiteSpace(ClientId))
        {
            ErrorMessage = _localization["settings.advanced.client_id_required"];
            return;
        }

        if (!await ConfirmAsync("settings.advanced.oauth_confirm_title", "settings.advanced.oauth_confirm_message", "settings.advanced.save_oauth", destructive: false).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await SignOutAllAsync(cancellationToken).ConfigureAwait(true);
            await _oauthOverride.SaveAsync(ClientId, ClientSecret, cancellationToken).ConfigureAwait(true);
            ClientSecret = string.Empty;
            HasStoredSecret = _oauthOverride.HasCustomClientSecret;
            StatusMessage = _localization["settings.advanced.oauth_saved"];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>"Dùng client mặc định": back to the build's client, after confirmation (signs accounts out).</summary>
    [RelayCommand]
    public async Task UseDefaultClientAsync(CancellationToken cancellationToken = default)
    {
        if (_oauthOverride is null)
        {
            return;
        }

        if (_oauthOverride.CustomClientId is not null &&
            !await ConfirmAsync("settings.advanced.oauth_confirm_title", "settings.advanced.oauth_confirm_message", "settings.advanced.use_default_client", destructive: false).ConfigureAwait(true))
        {
            _loading = true;
            UseCustomClient = true;
            _loading = false;
            return;
        }

        try
        {
            if (_oauthOverride.CustomClientId is not null)
            {
                await SignOutAllAsync(cancellationToken).ConfigureAwait(true);
            }

            await _oauthOverride.ResetAsync(cancellationToken).ConfigureAwait(true);
            _loading = true;
            UseCustomClient = false;
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            HasStoredSecret = false;
            _loading = false;
            StatusMessage = _localization["settings.advanced.oauth_reset"];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>"Xoá dữ liệu…" (SEC-DATA-3): cache, tokens, keys, queue, outbox and settings, after confirmation.</summary>
    [RelayCommand]
    public async Task WipeAllDataAsync(CancellationToken cancellationToken = default)
    {
        if (_wipeService is null)
        {
            return;
        }

        if (!await ConfirmAsync("settings.advanced.wipe_confirm_title", "settings.advanced.wipe_confirm_message", "settings.advanced.wipe_confirm_action", destructive: true).ConfigureAwait(true))
        {
            return;
        }

        IsWiping = true;
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _wipeService.WipeAllDataAsync(cancellationToken).ConfigureAwait(true);
            StatusMessage = _localization["settings.advanced.wipe_done"];
            WipeCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsWiping = false;
        }
    }

    /// <summary>"Mở thư mục" for the data folder.</summary>
    [RelayCommand]
    public void OpenDataFolder() => OpenFolder(DataDirectory);

    /// <summary>"Mở thư mục" for the log folder (SEC-LOG-1).</summary>
    [RelayCommand]
    public void OpenLogsFolder() => OpenFolder(LogsPath);

    private void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task<bool> ConfirmAsync(string titleKey, string messageKey, string confirmKey, bool destructive) =>
        _confirmation is not null && await _confirmation.ConfirmAsync(new ConfirmationRequest(
            _localization[titleKey], _localization[messageKey], _localization[confirmKey], destructive)).ConfigureAwait(true);

    private async Task SignOutAllAsync(CancellationToken cancellationToken)
    {
        if (_signOut is null || _accounts is null)
        {
            return;
        }

        foreach (var account in await _accounts.GetAllAccountsAsync(cancellationToken).ConfigureAwait(true))
        {
            await _signOut.SignOutAsync(account.Id, confirmed: true, cancellationToken).ConfigureAwait(true);
            await _accounts.DeleteAccountAsync(account.Id, cancellationToken).ConfigureAwait(true);
        }
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string FormatSize(long bytes)
    {
        var culture = _localization.CurrentCulture;
        return bytes >= 1024 * 1024
            ? _localization.Format("settings.advanced.size_mb", (bytes / (1024d * 1024d)).ToString("0.0", culture))
            : _localization.Format("settings.advanced.size_kb", Math.Max(0, bytes / 1024).ToString(CultureInfo.InvariantCulture));
    }
}
