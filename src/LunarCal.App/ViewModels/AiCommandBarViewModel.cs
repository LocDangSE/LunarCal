using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for the bottom AI command bar (Figma 01, FR-UI-5, FR-QUEUE-1, FR-QUEUE-3, FR-I18N-2).
/// </summary>
public sealed partial class AiCommandBarViewModel : ObservableObject, IDisposable
{
    private readonly IAiCommandStore _commandStore;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ISettingsStore? _settingsStore;
    private readonly IDisplayPreferences? _preferences;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommandCommand))]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string _detectedLanguage = "VI · EN";

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private bool _isOfflineBannerDismissed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommandCommand))]
    private bool _isSubmitting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommandCommand))]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Offline banner visibility (FR-QUEUE-3): shown when offline unless user explicitly dismissed it for this offline session.
    /// </summary>
    public bool IsOfflineBannerVisible => IsOffline && !IsOfflineBannerDismissed;

    public bool CanSubmit => !string.IsNullOrWhiteSpace(CommandText) && !IsSubmitting && IsEnabled;

    /// <summary>
    /// Raised when a new command is saved to the SQLite queue.
    /// </summary>
    public event EventHandler<AiCommand>? CommandEnqueued;

    /// <summary>
    /// Raised when navigation to AI Settings is requested (FR-CHAT-7).
    /// </summary>
    public event EventHandler? OpenAiSettingsRequested;

    public AiCommandBarViewModel(
        IAiCommandStore commandStore,
        ILocalizationService localizationService,
        INetworkStatusService? networkStatusService = null,
        ISettingsStore? settingsStore = null,
        IDisplayPreferences? preferences = null)
    {
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _networkStatusService = networkStatusService;
        _settingsStore = settingsStore;
        _preferences = preferences;

        if (_networkStatusService is not null)
        {
            _isOffline = !_networkStatusService.IsOnline;
            _networkStatusService.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        if (_settingsStore is not null)
        {
            _ = CheckGeminiEnabledAsync();
        }
    }

    private async Task CheckGeminiEnabledAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            IsEnabled = await _settingsStore.GetBoolAsync(SettingKeys.GeminiEnabled, defaultValue: true).ConfigureAwait(true);
        }
        catch
        {
            IsEnabled = true;
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, bool isOnline)
    {
        IsOffline = !isOnline;
        if (isOnline)
        {
            IsOfflineBannerDismissed = false;
        }

        OnPropertyChanged(nameof(IsOfflineBannerVisible));
    }

    partial void OnCommandTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            DetectedLanguage = "VI · EN";
            return;
        }

        var detected = LanguageDetector.Detect(value);
        DetectedLanguage = detected switch
        {
            "vi" => "VI",
            "en" => "EN",
            _ => "VI · EN",
        };
    }

    [RelayCommand]
    public void OpenAiSettings()
    {
        OpenAiSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void DismissOfflineBanner()
    {
        IsOfflineBannerDismissed = true;
        OnPropertyChanged(nameof(IsOfflineBannerVisible));
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SendCommandAsync(CancellationToken cancellationToken = default)
    {
        var text = CommandText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsSubmitting)
        {
            return;
        }

        // Enforce max length (FR-CHAT-3)
        if (text.Length > 4000)
        {
            text = text[..4000];
        }

        IsSubmitting = true;
        try
        {
            var inputLang = LanguageDetector.Detect(text);
            var typedAt = DateTimeOffset.Now;
            var timezone = _preferences?.TimeZone?.Id ?? TimeZoneInfo.Local.Id;

            var command = await _commandStore.EnqueueAsync(text, typedAt, timezone, inputLang, cancellationToken)
                .ConfigureAwait(true);

            CommandText = string.Empty;
            CommandEnqueued?.Invoke(this, command);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public void Dispose()
    {
        if (_networkStatusService is not null)
        {
            _networkStatusService.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
    }
}
