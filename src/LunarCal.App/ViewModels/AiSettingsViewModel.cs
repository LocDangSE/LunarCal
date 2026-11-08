using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for Settings > AI (Figma 04, FR-BYOK-1..6, SEC-KEY-4, SEC-KEY-6..11, ADR-0016).
/// Manages Gemini API key lifecycle and model selection with function-calling verification.
/// </summary>
public sealed partial class AiSettingsViewModel : ObservableObject
{
    private const string AiStudioUrl = "https://aistudio.google.com/apikey";

    private readonly IAiKeyService _aiKeyService;
    private readonly IAiModelService? _aiModelService;
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

    // --- Model Selection (FR-BYOK-5, ADR-0016 §2, §3) ---

    public ObservableCollection<AiModelInfo> AvailableModels { get; } = new();

    [ObservableProperty]
    private AiModelInfo? _selectedModel;

    [ObservableProperty]
    private bool _isLoadingModels;

    [ObservableProperty]
    private bool _isValidatingModel;

    [ObservableProperty]
    private string? _modelStatusMessage;

    [ObservableProperty]
    private bool _isModelStatusError;

    public AiSettingsViewModel(
        IAiKeyService aiKeyService,
        ILocalizationService localization,
        IClipboardService clipboardService,
        INetworkStatusService? networkStatusService = null)
        : this(aiKeyService, localization, clipboardService, networkStatusService, null)
    {
    }

    public AiSettingsViewModel(
        IAiKeyService aiKeyService,
        ILocalizationService localization,
        IClipboardService clipboardService,
        INetworkStatusService? networkStatusService,
        IAiModelService? aiModelService)
    {
        _aiKeyService = aiKeyService ?? throw new ArgumentNullException(nameof(aiKeyService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _networkStatusService = networkStatusService;
        _aiModelService = aiModelService;

        if (_networkStatusService is not null)
        {
            _isOffline = !_networkStatusService.IsOnline;
            _networkStatusService.NetworkAvailabilityChanged += (_, isOnline) =>
            {
                IsOffline = !isOnline;
            };
        }
    }

    /// <summary>Loads current key state and model selection from storage.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        HasKey = await _aiKeyService.HasKeyAsync(cancellationToken).ConfigureAwait(true);
        if (HasKey)
        {
            MaskedKey = await _aiKeyService.GetMaskedKeyAsync(cancellationToken).ConfigureAwait(true);
        }

        if (_aiModelService != null)
        {
            await LoadModelsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Loads available Gemini models and sets current active model (FR-BYOK-5).</summary>
    public async Task LoadModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_aiModelService == null)
        {
            return;
        }

        IsLoadingModels = true;
        try
        {
            var models = await _aiModelService.GetAvailableModelsAsync(cancellationToken).ConfigureAwait(true);
            AvailableModels.Clear();
            foreach (var m in models)
            {
                AvailableModels.Add(m);
            }

            var selectedId = await _aiModelService.GetSelectedModelIdAsync(cancellationToken).ConfigureAwait(true);
            SelectedModel = AvailableModels.FirstOrDefault(m => string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? AvailableModels.FirstOrDefault(m => m.IsDefault)
                ?? AvailableModels.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load models: {ex.Message}");
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    /// <summary>Selects and persists a new model after function-calling verification (FR-BYOK-5).</summary>
    [RelayCommand]
    public async Task SelectModelAsync(AiModelInfo? model)
    {
        if (model == null || _aiModelService == null || model == SelectedModel)
        {
            return;
        }

        IsValidatingModel = true;
        ModelStatusMessage = _localization["settings.ai.model_validating"];
        IsModelStatusError = false;

        try
        {
            var success = await _aiModelService.SetSelectedModelAsync(model.Id, validateFunctionCalling: true, CancellationToken.None).ConfigureAwait(true);
            if (success)
            {
                SelectedModel = model;
                IsModelStatusError = false;
                ModelStatusMessage = _localization["settings.ai.model_saved"];
            }
            else
            {
                IsModelStatusError = true;
                ModelStatusMessage = _localization["settings.ai.err_function_calling_unsupported"];
            }
        }
        catch (Exception ex)
        {
            IsModelStatusError = true;
            ModelStatusMessage = _localization.Format("settings.ai.err_unexpected", ex.Message);
        }
        finally
        {
            IsValidatingModel = false;
        }
    }

    /// <summary>Refreshes the available models list from API.</summary>
    [RelayCommand]
    public async Task RefreshModelsAsync()
    {
        await LoadModelsAsync(CancellationToken.None).ConfigureAwait(true);
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

                if (_aiModelService != null)
                {
                    await LoadModelsAsync(cancellationToken).ConfigureAwait(true);
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

                if (_aiModelService != null)
                {
                    await LoadModelsAsync(cancellationToken).ConfigureAwait(true);
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

    [RelayCommand]
    public void StartEditing()
    {
        IsEditing = true;
        StatusMessage = null;
        IsClipboardCleared = false;
        IsSuspectLeaked = false;
    }

    [RelayCommand]
    public void CancelEditing()
    {
        IsEditing = false;
        StatusMessage = null;
        IsClipboardCleared = false;
    }

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

        if (_aiModelService != null)
        {
            await LoadModelsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

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

        if (_aiModelService != null)
        {
            await LoadModelsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public static void OpenAiStudio()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AiStudioUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open browser: {ex.Message}");
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
