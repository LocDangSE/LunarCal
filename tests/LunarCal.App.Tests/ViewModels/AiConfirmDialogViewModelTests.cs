using System;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class AiConfirmDialogViewModelTests
{
    private static readonly DateTimeOffset _typedAt = new(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7));
    private readonly LocalizationService _localization = new(SupportedCultures.Vietnamese);

    private static AiCommand CreateTestCommand(string text = "họp chiến lược vào thứ năm tuần sau 9h")
    {
        return new AiCommand
        {
            Id = "cmd-1000",
            DisplayNo = 1000,
            Text = text,
            TypedAt = _typedAt,
            TimeZone = "Asia/Ho_Chi_Minh",
            InputLang = "vi",
            Status = AiCommandStatus.NeedsConfirmation,
            CreatedAt = _typedAt,
            UpdatedAt = _typedAt
        };
    }

    [Fact]
    public void FR_CONFIRM_1_Initializes_with_correct_texts_dates_and_status()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp chiến lược Q4", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");

        var confidence = new AiConfidenceResult(95, AiConfidenceLevel.High, []);
        var validation = AiBusinessValidationResult.Success();

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);

        vm.OriginalText.Should().Be("họp chiến lược vào thứ năm tuần sau 9h");
        vm.InputLangDisplay.Should().Be("Tiếng Việt");
        vm.EventTitle.Should().Be("Họp chiến lược Q4");
        vm.EventTimeDisplay.Should().Be("09:00 – 10:00");
        vm.ConfidenceScore.Should().Be(95);
        vm.ConfidenceLevel.Should().Be(AiConfidenceLevel.High);
        vm.ConfidenceDisplay.Should().Contain("95%").And.Contain("Rõ ràng");
        vm.ValidationSeverity.Should().Be(AiBusinessValidationSeverity.Pass);
        vm.CanConfirm.Should().BeTrue();
    }

    [Fact]
    public void FR_CONFIRM_3_Includes_lunar_date_when_lunar_enabled()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");

        var confidence = new AiConfidenceResult(92, AiConfidenceLevel.High, []);
        var validation = AiBusinessValidationResult.Success();

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);

        vm.IsLunarVisible.Should().BeTrue();
        // 22/10/2026 solar is 13/9 lunar in Bính Ngọ
        vm.EventLunarDateDisplay.Should().Contain("13/9").And.Contain("Âm lịch");
    }

    [Fact]
    public void FR_CONFIRM_3_Hides_lunar_date_when_lunar_calendar_is_disabled()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp", "start": "2026-10-22T09:00:00+07:00"}""");

        var confidence = new AiConfidenceResult(90, AiConfidenceLevel.High, []);
        var validation = AiBusinessValidationResult.Success();

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: false, isTrainingOptIn: false);

        vm.IsLunarVisible.Should().BeFalse();
        vm.EventLunarDateDisplay.Should().BeEmpty();
    }

    [Fact]
    public void FR_CONFIRM_4_Low_confidence_below_60_percent_locks_confirmation_button()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand("họp khoảng tuần sau");
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "", "start": "2026-10-22T09:00:00+07:00"}""");

        var confidence = new AiConfidenceResult(45, AiConfidenceLevel.Low, ["Missing summary", "Ambiguity keyword"]);
        var validation = AiBusinessValidationResult.Success();

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);

        vm.ConfidenceScore.Should().Be(45);
        vm.ConfidenceLevel.Should().Be(AiConfidenceLevel.Low);
        vm.CanConfirm.Should().BeFalse();
        vm.CanConfirmDisabledReason.Should().NotBeNullOrWhiteSpace();

        // Attempting to confirm does not succeed
        vm.ConfirmCommand.Execute(null);
        vm.DialogResult.Should().BeNull();
    }

    [Fact]
    public void FR_CONFIRM_5_Validation_blocking_error_locks_confirmation_button()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp", "start": "2026-10-22T10:00:00+07:00", "end": "2026-10-22T09:00:00+07:00"}""");

        var confidence = new AiConfidenceResult(80, AiConfidenceLevel.Medium, []);
        var validation = AiBusinessValidationResult.WithErrors(
        [
            new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Giờ kết thúc phải sau giờ bắt đầu.")
        ]);

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);

        vm.ValidationSeverity.Should().Be(AiBusinessValidationSeverity.Error);
        vm.CanConfirm.Should().BeFalse();
        vm.CanConfirmDisabledReason.Should().Contain("kết thúc phải sau");

        // Attempting to confirm does not succeed
        vm.ConfirmCommand.Execute(null);
        vm.DialogResult.Should().BeNull();
    }

    [Fact]
    public void FR_CONFIRM_6_Privacy_notice_reflects_opt_in_setting()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, "{}");
        var confidence = new AiConfidenceResult(90, AiConfidenceLevel.High, []);
        var validation = AiBusinessValidationResult.Success();

        // When opt-in is false
        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);
        vm.PrivacyNotice.Should().Contain("Opt-in đang tắt");

        // When opt-in is true
        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: true);
        vm.PrivacyNotice.Should().Contain("Opt-in đang bật");
    }

    [Fact]
    public void FR_CONFIRM_6_Edit_command_triggers_request_edit_flag()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand();
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, "{}");
        var confidence = new AiConfidenceResult(90, AiConfidenceLevel.High, []);
        var validation = AiBusinessValidationResult.Success();

        vm.Initialize(command, toolCall, confidence, validation, isLunarEnabled: true, isTrainingOptIn: false);

        var closeRequestedTriggered = false;
        vm.CloseRequested += (_, result) =>
        {
            closeRequestedTriggered = true;
            result.Should().Be(false);
        };

        vm.EditCommand.Execute(null);

        vm.RequestEdit.Should().BeTrue();
        vm.DialogResult.Should().Be(false);
        closeRequestedTriggered.Should().BeTrue();
    }

    [Fact]
    public void FR_CONFIRM_8_InitializeForClarification_sets_clarification_mode_and_question_and_quick_answers()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand("họp team vào ngày mai");

        vm.InitializeForClarification(
            command,
            "Bạn muốn họp lúc mấy giờ?",
            quickAnswers: ["08:30", "10:00", "14:30", "Cả ngày"],
            isMissingTime: true);

        vm.IsClarificationMode.Should().BeTrue();
        vm.ClarificationQuestion.Should().Be("Bạn muốn họp lúc mấy giờ?");
        vm.QuickAnswers.Should().Equal("08:30", "10:00", "14:30", "Cả ngày");
        vm.IsMissingTime.Should().BeTrue();
        vm.EventTimeDisplay.Should().Be("Chưa rõ giờ cụ thể");
        vm.CanConfirm.Should().BeFalse();
        vm.CanSubmitClarification.Should().BeFalse();
    }

    [Fact]
    public void FR_CONFIRM_8_SelectQuickAnswerCommand_populates_clarification_answer_and_enables_submit()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand("họp team");

        vm.InitializeForClarification(command, "Mấy giờ họp?", isMissingTime: true);

        vm.CanSubmitClarification.Should().BeFalse();

        vm.SelectQuickAnswerCommand.Execute("14:00");

        vm.ClarificationAnswer.Should().Be("14:00");
        vm.CanSubmitClarification.Should().BeTrue();
    }

    [Fact]
    public void FR_CONFIRM_8_SubmitClarificationCommand_marks_submitted_and_closes_dialog()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand("họp team");

        vm.InitializeForClarification(command, "Mấy giờ họp?", isMissingTime: true);
        vm.ClarificationAnswer = "10:30 sáng";

        var closeRequestedTriggered = false;
        vm.CloseRequested += (_, result) =>
        {
            closeRequestedTriggered = true;
            result.Should().Be(true);
        };

        vm.SubmitClarificationCommand.Execute(null);

        vm.ClarificationSubmitted.Should().BeTrue();
        vm.DialogResult.Should().Be(true);
        closeRequestedTriggered.Should().BeTrue();
    }

    [Fact]
    public void FR_CONFIRM_8_EditCommand_is_ignored_when_in_clarification_mode()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var command = CreateTestCommand("họp team");

        vm.InitializeForClarification(command, "Mấy giờ họp?", isMissingTime: true);

        var closeRequestedTriggered = false;
        vm.CloseRequested += (_, _) => closeRequestedTriggered = true;

        vm.EditCommand.Execute(null);

        vm.RequestEdit.Should().BeFalse();
        closeRequestedTriggered.Should().BeFalse();
    }

    [Fact]
    public void FR_CONFIRM_1_Shows_title_and_all_day_from_schema_fields_title_and_allDay()
    {
        var vm = new AiConfirmDialogViewModel(_localization);
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title": "Giỗ ông nội", "start": "2026-10-22T00:00:00+07:00", "allDay": true}""");

        vm.Initialize(
            CreateTestCommand(),
            toolCall,
            new AiConfidenceResult(95, AiConfidenceLevel.High, []),
            AiBusinessValidationResult.Success(),
            isLunarEnabled: true,
            isTrainingOptIn: false);

        vm.EventTitle.Should().Be("Giỗ ông nội");
        vm.EventTimeDisplay.Should().Be("Cả ngày");
    }
}
