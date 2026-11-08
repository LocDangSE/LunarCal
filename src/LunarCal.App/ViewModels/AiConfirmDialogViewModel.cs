using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunarCal.App.Localization;
using LunarCal.Core.Chat;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for the AI Confirmation Dialog (FR-CONFIRM-1..6, FR-CHAT-4, Figma 02 2:2 / 2:494)
/// and AI Clarification State (FR-CONFIRM-8, Figma S16 46:370, dialog 46:862).
/// Presents user input, analysis breakdown, local confidence score, business validation, and privacy notice.
/// Strictly locks confirmation when confidence is below 60% or when validation reports blocking errors.
/// In clarification mode, replaces the "Sửa" button with a focused short answer input and quick suggestions.
/// </summary>
public partial class AiConfirmDialogViewModel : ObservableObject
{
    private readonly ILocalizationService? _localization;

    public event EventHandler<bool?>? CloseRequested;

    [ObservableProperty]
    private string _originalText = string.Empty;

    [ObservableProperty]
    private string _inputLang = "vi";

    [ObservableProperty]
    private string _inputLangDisplay = "Tiếng Việt";

    [ObservableProperty]
    private DateTimeOffset _typedAt = DateTimeOffset.Now;

    [ObservableProperty]
    private string _typedAtDisplay = string.Empty;

    [ObservableProperty]
    private string _actionName = string.Empty;

    [ObservableProperty]
    private string _eventTitle = string.Empty;

    [ObservableProperty]
    private string _eventDateDisplay = string.Empty;

    [ObservableProperty]
    private string _eventLunarDateDisplay = string.Empty;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private string _eventTimeDisplay = string.Empty;

    [ObservableProperty]
    private string _recurrenceDisplay = string.Empty;

    [ObservableProperty]
    private string _remindersDisplay = string.Empty;

    [ObservableProperty]
    private string _targetCalendarName = "Cá nhân";

    [ObservableProperty]
    private string _targetCalendarColor = "#2F6BFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanConfirmDisabledReason))]
    [NotifyPropertyChangedFor(nameof(ConfidenceProgressRatio))]
    private int _confidenceScore = 100;

    [ObservableProperty]
    private AiConfidenceLevel _confidenceLevel = AiConfidenceLevel.High;

    [ObservableProperty]
    private string _confidenceDisplay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanConfirmDisabledReason))]
    private AiBusinessValidationSeverity _validationSeverity = AiBusinessValidationSeverity.Pass;

    [ObservableProperty]
    private string _validationSummary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AiBusinessValidationMessage> _validationMessages = [];

    [ObservableProperty]
    private bool _isTrainingOptIn;

    [ObservableProperty]
    private string _privacyNotice = string.Empty;

    [ObservableProperty]
    private bool? _dialogResult;

    [ObservableProperty]
    private bool _requestEdit;

    // S16 Clarification state properties (FR-CONFIRM-8)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private bool _isClarificationMode;

    [ObservableProperty]
    private string _clarificationQuestion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitClarification))]
    private string _clarificationAnswer = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _quickAnswers = ["09:00", "14:00", "19:00", "Cả ngày"];

    [ObservableProperty]
    private bool _isMissingTime;

    [ObservableProperty]
    private bool _clarificationSubmitted;

    public bool CanSubmitClarification => !string.IsNullOrWhiteSpace(ClarificationAnswer);

    public bool CanConfirm => !IsClarificationMode && ConfidenceScore >= 60 && ValidationSeverity != AiBusinessValidationSeverity.Error;

    public double ConfidenceProgressRatio => Math.Clamp(ConfidenceScore / 100.0, 0.0, 1.0);

    public string CanConfirmDisabledReason
    {
        get
        {
            if (IsClarificationMode)
            {
                return GetString("ai_confirm.clarify_submit", "Trả lời câu hỏi của AI để tiếp tục");
            }

            if (ValidationSeverity == AiBusinessValidationSeverity.Error)
            {
                return GetString("ai_confirm.val_end_before_start", "Lỗi: Giờ kết thúc phải sau giờ bắt đầu.");
            }

            if (ConfidenceScore < 60)
            {
                return GetString("ai_confirm.btn_confirm_disabled_hint", "Cần sửa trước khi xác nhận khi độ tin cậy dưới 60% hoặc có lỗi");
            }

            return string.Empty;
        }
    }

    public AiConfirmDialogViewModel(ILocalizationService? localization = null)
    {
        _localization = localization;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm)
        {
            return;
        }

        DialogResult = true;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void Edit()
    {
        if (IsClarificationMode)
        {
            return; // S16 removes Edit button
        }

        RequestEdit = true;
        DialogResult = false;
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void SelectQuickAnswer(string? answer)
    {
        if (!string.IsNullOrWhiteSpace(answer))
        {
            ClarificationAnswer = answer;
        }
    }

    [RelayCommand]
    private void SubmitClarification()
    {
        if (!CanSubmitClarification)
        {
            return;
        }

        ClarificationSubmitted = true;
        DialogResult = true;
        CloseRequested?.Invoke(this, true);
    }

    /// <summary>
    /// Populates view model properties from tool call, confidence score, and validation results.
    /// </summary>
    public void Initialize(
        AiCommand command,
        ToolCall toolCall,
        AiConfidenceResult confidence,
        AiBusinessValidationResult validation,
        bool isLunarEnabled,
        bool isTrainingOptIn,
        string targetCalendarName = "Cá nhân",
        string targetCalendarColor = "#2F6BFF",
        bool isClarificationMode = false,
        string? clarificationQuestion = null,
        IReadOnlyList<string>? quickAnswers = null,
        bool isMissingTime = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(validation);

        OriginalText = command.Text;
        InputLang = command.InputLang ?? "vi";
        InputLangDisplay = InputLang switch
        {
            "en" => "English",
            "mixed" => "Hỗn hợp (VI · EN)",
            _ => "Tiếng Việt"
        };

        TypedAt = command.TypedAt;
        TypedAtDisplay = command.TypedAt.ToString("dd/MM/yyyy HH:mm 'GMT'zzz", CultureInfo.InvariantCulture);

        ConfidenceScore = confidence.Score;
        ConfidenceLevel = confidence.Level;
        ConfidenceDisplay = confidence.Level switch
        {
            AiConfidenceLevel.High => $"{confidence.Score}% · {GetString("ai_confirm.confidence_high", "Rõ ràng")}",
            AiConfidenceLevel.Medium => $"{confidence.Score}% · {GetString("ai_confirm.confidence_medium", "Cần kiểm tra")}",
            _ => $"{confidence.Score}% · {GetString("ai_confirm.confidence_low", "Không chắc")}"
        };

        ValidationSeverity = validation.Severity;
        ValidationMessages = validation.Messages;
        ValidationSummary = validation.Messages.Count > 0
            ? validation.Messages[0].Message
            : GetString("ai_confirm.val_all_good", "Đã qua kiểm tra nghiệp vụ: giờ hợp lệ, không xung đột với lịch hiện tại.");

        IsLunarVisible = isLunarEnabled;
        IsTrainingOptIn = isTrainingOptIn;
        PrivacyNotice = isTrainingOptIn
            ? GetString("confirm.privacy.on", "Opt-in đang bật: bản đã loại thông tin nhận dạng của yêu cầu này sẽ được gửi để huấn luyện AI.")
            : GetString("confirm.privacy.off", "Opt-in đang tắt: không gửi dữ liệu huấn luyện khi bạn xác nhận.");

        TargetCalendarName = targetCalendarName;
        TargetCalendarColor = targetCalendarColor;

        ActionName = toolCall.Name switch
        {
            ToolDefinitions.CreateEvent => GetString("ai_confirm.action_create_event", "Tạo sự kiện mới"),
            ToolDefinitions.CreateRecurringEvent => GetString("ai_confirm.action_create_event", "Tạo sự kiện mới"),
            ToolDefinitions.CreateLunarRecurringEvent => GetString("ai_confirm.action_create_event", "Tạo sự kiện mới"),
            ToolDefinitions.UpdateEvent => GetString("ai_confirm.action_update_event", "Cập nhật sự kiện"),
            ToolDefinitions.DeleteEvent => GetString("ai_confirm.action_delete_event", "Xóa sự kiện"),
            ToolDefinitions.BulkCreateEvents => GetString("ai_confirm.action_bulk_create", "Tạo nhiều sự kiện"),
            _ => toolCall.Name
        };

        RecurrenceDisplay = GetString("ai_confirm.no_recurrence", "Không lặp");
        RemindersDisplay = GetString("ai_confirm.default_reminder", "10 phút trước");

        ParseArguments(toolCall);

        // Clarification setup (FR-CONFIRM-8, S16)
        IsClarificationMode = isClarificationMode;
        IsMissingTime = isMissingTime;

        if (quickAnswers is not null && quickAnswers.Count > 0)
        {
            QuickAnswers = quickAnswers;
        }

        if (isClarificationMode)
        {
            ClarificationQuestion = !string.IsNullOrWhiteSpace(clarificationQuestion)
                ? clarificationQuestion
                : GetString("ai_confirm.missing_time_warning", "Bạn muốn sự kiện diễn ra lúc mấy giờ?");

            if (isMissingTime)
            {
                EventTimeDisplay = GetString("ai_confirm.missing_time_warning", "Chưa rõ giờ cụ thể");
            }
        }
    }

    /// <summary>
    /// FR-CONFIRM-8: Directly initializes the view model in Clarification Mode (S16 46:370, dialog 46:862).
    /// </summary>
    public void InitializeForClarification(
        AiCommand command,
        string question,
        IReadOnlyList<string>? quickAnswers = null,
        ToolCall? preliminaryToolCall = null,
        bool isMissingTime = true,
        bool isLunarEnabled = true,
        bool isTrainingOptIn = false,
        string targetCalendarName = "Cá nhân",
        string targetCalendarColor = "#2F6BFF")
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var toolCall = preliminaryToolCall ?? new ToolCall(
            ToolDefinitions.CreateEvent,
            JsonSerializer.Serialize(new
            {
                summary = command.Text,
                start = command.TypedAt.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
            }));

        var confidence = new AiConfidenceResult(
            50,
            AiConfidenceLevel.Low,
            ["Model requested additional information (clarification needed)."]);

        var validation = isMissingTime
            ? AiBusinessValidationResult.WithWarnings([new AiBusinessValidationMessage(AiBusinessValidationSeverity.Warning, "Chưa rõ giờ cụ thể, cần bổ sung trước khi tiếp tục.")])
            : AiBusinessValidationResult.Success();

        Initialize(
            command,
            toolCall,
            confidence,
            validation,
            isLunarEnabled,
            isTrainingOptIn,
            targetCalendarName,
            targetCalendarColor,
            isClarificationMode: true,
            clarificationQuestion: question,
            quickAnswers: quickAnswers,
            isMissingTime: isMissingTime);
    }

    private string GetString(string key, string fallback)
    {
        if (_localization is null)
        {
            return fallback;
        }

        return _localization.GetString(key, _localization.CurrentCulture) ?? fallback;
    }

    private void ParseArguments(ToolCall toolCall)
    {
        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("summary", out var summaryProp))
            {
                EventTitle = summaryProp.GetString() ?? string.Empty;
            }

            var isAllDay = root.TryGetProperty("all_day", out var allDayProp) && allDayProp.GetBoolean();

            if (root.TryGetProperty("start", out var startProp) &&
                DateTimeOffset.TryParse(startProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            {
                var localStart = start.ToOffset(TypedAt.Offset);
                EventDateDisplay = localStart.ToString("dddd, dd/MM/yyyy", CultureInfo.CurrentCulture);

                if (IsLunarVisible)
                {
                    try
                    {
                        var lunarDate = LunarCalendar.ToLunar(new DateOnly(localStart.Year, localStart.Month, localStart.Day));
                        var leapMarker = lunarDate.IsLeapMonth ? "n" : string.Empty;
                        EventLunarDateDisplay = $"{lunarDate.Day}/{lunarDate.Month}{leapMarker} Âm lịch";
                    }
                    catch
                    {
                        EventLunarDateDisplay = string.Empty;
                    }
                }
                else
                {
                    EventLunarDateDisplay = string.Empty;
                }

                if (isAllDay)
                {
                    EventTimeDisplay = GetString("ai_confirm.all_day", "Cả ngày");
                }
                else if (root.TryGetProperty("end", out var endProp) &&
                         DateTimeOffset.TryParse(endProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    var localEnd = end.ToOffset(TypedAt.Offset);
                    EventTimeDisplay = $"{localStart:HH:mm} – {localEnd:HH:mm}";
                }
                else
                {
                    var localEnd = localStart.AddHours(1);
                    EventTimeDisplay = $"{localStart:HH:mm} – {localEnd:HH:mm}";
                }
            }

            if (root.TryGetProperty("recurrence", out var recProp) && !string.IsNullOrWhiteSpace(recProp.GetString()))
            {
                RecurrenceDisplay = recProp.GetString()!;
            }

            if (root.TryGetProperty("reminders", out var remProp) && remProp.ValueKind == JsonValueKind.Array)
            {
                var rems = remProp.EnumerateArray()
                    .Select(r => $"{r.GetInt32()} phút trước")
                    .ToList();
                if (rems.Count > 0)
                {
                    RemindersDisplay = string.Join(", ", rems);
                }
            }
        }
        catch
        {
            // Fallback gracefully on arguments parsing errors
        }
    }
}
