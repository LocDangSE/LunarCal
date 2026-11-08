using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for Settings > AI (Figma 04, FR-BYOK-1..4, FR-BYOK-6, SEC-KEY-4, SEC-KEY-6..11, ADR-0016).
/// </summary>
public sealed partial class AiSettingsViewModel : ObservableObject
{
    private const string AiStudioUrl = "https://aistudio.google.com/apikey";

    private readonly IAiKeyService _aiKeyService;
    private readonly ILocalizationService _localization;
    private readonly IClipboardService _clipboardService;
    private readonly INetworkStatusService? _networkStatusService;

    [ObservableProperty]
    private bool _hasKey;

    [ObservableProperty]
    private string? _maskedKey;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isClipboardCleared;

    [ObservableProperty]
    private bool _isSuspectLeaked;

    public AiSettingsViewModel(
        IAiKeyService aiKeyService,
        ILocalizationService localization,
        IClipboardService clipboardService,
        INetworkStatusService? networkStatusService = null)
    {
        _aiKeyService = aiKeyService ?? throw new ArgumentNullException(nameof(aiKeyService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _networkStatusService = networkStatusService;

        if (_networkStatusService is not null)
        {
            _isOffline = !_networkStatusService.IsOnline;
            _networkStatusService.NetworkAvailabilityChanged += (_, isOnline) =>
            {
                IsOffline = !isOnline;
            };
        }
    }

    /// <summary>Loads current key state from secure storage.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        HasKey = await _aiKeyService.HasKeyAsync(cancellationToken).ConfigureAwait(true);
        if (HasKey)
        {
            MaskedKey = await _aiKeyService.GetMaskedKeyAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Validates the provided key against the Gemini API and saves it upon success (SEC-KEY-6).</summary>
    public async Task<bool> CheckAndSaveKeyAsync(string? rawKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            StatusMessage = _localization["settings.ai.err_empty"];
            IsStatusError = true;
            return false;
        }

        IsChecking = true;
        StatusMessage = _localization["settings.ai.checking"];
        IsStatusError = false;
        IsClipboardCleared = false;

        try
        {
            var result = await _aiKeyService.ValidateAndSaveKeyAsync(rawKey, saveWithoutValidation: false, cancellationToken).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                HasKey = true;
                MaskedKey = _aiKeyService.MaskKey(rawKey);
                IsEditing = false;
                StatusMessage = _localization["settings.ai.status_saved"];
                IsStatusError = false;
                IsSuspectLeaked = false;

                if (_clipboardService.ClearIfMatches(rawKey))
                {
                    IsClipboardCleared = true;
                }

                return true;
            }

            IsStatusError = true;
            StatusMessage = ResolveErrorMessage(result);
            if (result.Status == KeyValidationStatus.NetworkError)
            {
                IsOffline = true;
            }

            return false;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Saves the key without network validation when offline (FR-BYOK-6, ADR-0016 §6.6).</summary>
    public async Task<bool> SaveOfflineAsync(string? rawKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            StatusMessage = _localization["settings.ai.err_empty"];
            IsStatusError = true;
            return false;
        }

        IsChecking = true;
        IsClipboardCleared = false;

        try
        {
            var result = await _aiKeyService.ValidateAndSaveKeyAsync(rawKey, saveWithoutValidation: true, cancellationToken).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                HasKey = true;
                MaskedKey = _aiKeyService.MaskKey(rawKey);
                IsEditing = false;
                StatusMessage = _localization["settings.ai.status_saved"];
                IsStatusError = false;
                IsSuspectLeaked = false;

                if (_clipboardService.ClearIfMatches(rawKey))
                {
                    IsClipboardCleared = true;
                }

                return true;
            }

            IsStatusError = true;
            StatusMessage = ResolveErrorMessage(result);
            return false;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Enables editing mode to enter a new key.</summary>
    [RelayCommand]
    public void StartEditing()
    {
        IsEditing = true;
        StatusMessage = null;
        IsClipboardCleared = false;
        IsSuspectLeaked = false;
    }

    /// <summary>Cancels editing mode.</summary>
    [RelayCommand]
    public void CancelEditing()
    {
        IsEditing = false;
        StatusMessage = null;
    }

    /// <summary>Deletes the current key from secret storage (SEC-KEY-4, FR-BYOK-4).</summary>
    [RelayCommand]
    public async Task DeleteKeyAsync(CancellationToken cancellationToken = default)
    {
        await _aiKeyService.DeleteKeyAsync(cancellationToken).ConfigureAwait(true);
        HasKey = false;
        MaskedKey = null;
        IsEditing = false;
        StatusMessage = null;
        IsClipboardCleared = false;
        IsSuspectLeaked = false;
    }

    /// <summary>Reacts to suspected key leak: deletes key immediately and pauses AI queue (SEC-KEY-7, FR-BYOK-4).</summary>
    [RelayCommand]
    public async Task SuspectLeakedAsync(CancellationToken cancellationToken = default)
    {
        await _aiKeyService.SuspectLeakedAsync(cancellationToken).ConfigureAwait(true);
        HasKey = false;
        MaskedKey = null;
        IsEditing = false;
        StatusMessage = null;
        IsClipboardCleared = false;
        IsSuspectLeaked = true;
    }

    /// <summary>Opens Google AI Studio key creation page in system browser.</summary>
    [RelayCommand]
    public static void OpenAiStudio()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AiStudioUrl) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failure to open system browser
        }
    }

    private string ResolveErrorMessage(KeyValidationResult result)
    {
        return result.Status switch
        {
            KeyValidationStatus.EmptyKey => _localization["settings.ai.err_empty"],
            KeyValidationStatus.InvalidKey => _localization["settings.ai.err_invalid"],
            KeyValidationStatus.LeakedKey => _localization["settings.ai.err_leaked"],
            KeyValidationStatus.FreeTierVerificationRequired => _localization["settings.ai.err_freetier"],
            KeyValidationStatus.PreconditionFailed => _localization["settings.ai.err_precondition"],
            KeyValidationStatus.QuotaExceeded => _localization["settings.ai.err_quota"],
            KeyValidationStatus.NetworkError => _localization["settings.ai.err_network"],
            _ => _localization.Format("settings.ai.err_unexpected", result.Message ?? "Unknown")
        };
    }
}
