using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Chat;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing an AI command in the queue drawer (Figma 03, FR-QUEUE-2, FR-QUEUE-6, FR-CHAT-7).
/// </summary>
public sealed partial class AiCommandItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private long _displayNo;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _typedAt;

    [ObservableProperty]
    private string _status = AiCommandStatus.Pending;

    [ObservableProperty]
    private int _retryCount;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string _inputLang = "vi";

    [ObservableProperty]
    private bool _isDestructive;

    public string DisplayNumberText => $"#{DisplayNo}";

    public string TypedAtFormatted => TypedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string RelativeTimeText
    {
        get
        {
            var elapsed = DateTimeOffset.Now - TypedAt;
            if (elapsed.TotalMinutes < 1)
            {
                return _localizationService["ai_queue.time_just_now"];
            }

            if (elapsed.TotalHours < 1)
            {
                return _localizationService.Format("ai_queue.time_minutes_ago", (int)elapsed.TotalMinutes);
            }

            return TypedAtFormatted;
        }
    }

    public string StatusText => Status switch
    {
        AiCommandStatus.Pending => _localizationService["ai_queue.status_pending"],
        AiCommandStatus.Processing => _localizationService["ai_queue.status_processing"],
        AiCommandStatus.NeedsConfirmation => _localizationService["ai_queue.status_needs_confirmation"],
        AiCommandStatus.Error => _localizationService["ai_queue.status_error"],
        AiCommandStatus.Completed => _localizationService["ai_queue.status_completed"],
        AiCommandStatus.Cancelled => _localizationService["ai_queue.status_cancelled"],
        _ => Status,
    };

    public string DestructiveWarningText => _localizationService["ai_queue.destructive_warning"];

    /// <summary>
    /// Whether this item failed due to an API Key issue (invalid, missing, leaked, billing, model not found).
    /// Used to show "Mở Cài đặt AI" action button (FR-CHAT-7).
    /// </summary>
    public bool IsApiKeyError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastError))
            {
                return false;
            }

            var lower = LastError.ToLowerInvariant();
            return lower.Contains("invalid_key", StringComparison.Ordinal)
                || lower.Contains("missing_key", StringComparison.Ordinal)
                || lower.Contains("leaked_key", StringComparison.Ordinal)
                || lower.Contains("free_tier", StringComparison.Ordinal)
                || lower.Contains("model_not_found", StringComparison.Ordinal)
                || lower.Contains("api key", StringComparison.Ordinal)
                || lower.Contains("reported as leaked", StringComparison.Ordinal)
                || lower.Contains("api_key", StringComparison.Ordinal)
                || lower.Contains("unauthorized", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Whether this item was blocked by safety filters.
    /// </summary>
    public bool IsSafetyBlocked
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastError))
            {
                return false;
            }

            var lower = LastError.ToLowerInvariant();
            return lower.Contains("safety", StringComparison.Ordinal)
                || lower.Contains("blocked", StringComparison.Ordinal)
                || lower.Contains("an toàn", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Whether this item failed due to exhausting the daily free quota.
    /// </summary>
    public bool IsDailyQuotaExceeded
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastError))
            {
                return false;
            }

            var lower = LastError.ToLowerInvariant();
            return lower.Contains("daily_quota", StringComparison.Ordinal)
                || (lower.Contains("daily", StringComparison.Ordinal) && lower.Contains("quota", StringComparison.Ordinal))
                || lower.Contains("hạn mức ngày", StringComparison.Ordinal);
        }
    }

    public string? RetryInfoText
    {
        get
        {
            if (Status != AiCommandStatus.Error)
            {
                return null;
            }

            // If it's an unrecoverable error, display the localized message directly
            if (IsApiKeyError || IsSafetyBlocked || IsDailyQuotaExceeded)
            {
                return GetLocalizedErrorText();
            }

            if (RetryCount > 0 && RetryCount <= 5)
            {
                return _localizationService.Format("ai_queue.retry_format", RetryCount, 5, 8);
            }

            return GetLocalizedErrorText();
        }
    }

    private string GetLocalizedErrorText()
    {
        if (string.IsNullOrWhiteSpace(LastError))
        {
            return _localizationService["ai_error.unknown"];
        }

        // Try direct resx lookup if LastError is a localization key
        var localized = _localizationService[LastError];
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, LastError, StringComparison.Ordinal))
        {
            return localized;
        }

        return LastError;
    }

    // Action visibility based on FR-QUEUE-6 & FR-CHAT-7
    public bool CanView => !string.IsNullOrEmpty(Id);

    public bool CanConfirm => Status == AiCommandStatus.NeedsConfirmation;

    public bool CanRetry => Status == AiCommandStatus.Error && !IsApiKeyError && !IsSafetyBlocked && !IsDailyQuotaExceeded;

    public bool CanEdit => Status == AiCommandStatus.Error;

    public bool CanOpenAiSettings => Status == AiCommandStatus.Error && IsApiKeyError;

    public bool CanCancel => Status == AiCommandStatus.Pending
                          || Status == AiCommandStatus.Processing
                          || Status == AiCommandStatus.NeedsConfirmation
                          || Status == AiCommandStatus.Error;

    public bool IsPending => Status == AiCommandStatus.Pending;
    public bool IsProcessing => Status == AiCommandStatus.Processing;
    public bool IsNeedsConfirmation => Status == AiCommandStatus.NeedsConfirmation;
    public bool IsError => Status == AiCommandStatus.Error;
    public bool IsCompleted => Status == AiCommandStatus.Completed;
    public bool IsCancelled => Status == AiCommandStatus.Cancelled;

    public AiCommandItemViewModel(AiCommand command, ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(command);
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        _id = command.Id;
        _displayNo = command.DisplayNo;
        _text = command.Text;
        _typedAt = command.TypedAt;
        _status = command.Status;
        _retryCount = command.RetryCount;
        _lastError = command.LastError;
        _inputLang = command.InputLang;

        _isDestructive = DetectDestructiveIntent(command.Text);
    }

    private static bool DetectDestructiveIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        return lower.Contains("xoá", StringComparison.Ordinal)
            || lower.Contains("xóa", StringComparison.Ordinal)
            || lower.Contains("huỷ", StringComparison.Ordinal)
            || lower.Contains("hủy", StringComparison.Ordinal)
            || lower.Contains("delete", StringComparison.Ordinal)
            || lower.Contains("remove", StringComparison.Ordinal)
            || lower.Contains("cancel", StringComparison.Ordinal);
    }

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanOpenAiSettings));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsNeedsConfirmation));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(RetryInfoText));
    }

    partial void OnLastErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(IsApiKeyError));
        OnPropertyChanged(nameof(IsSafetyBlocked));
        OnPropertyChanged(nameof(IsDailyQuotaExceeded));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanOpenAiSettings));
        OnPropertyChanged(nameof(RetryInfoText));
    }
}
