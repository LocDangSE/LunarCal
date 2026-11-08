using System;
using AwesomeAssertions;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

public sealed class AiConfidenceEvaluatorTests
{
    private static readonly DateTimeOffset _anchorTime = new(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7));

    [Fact]
    public void FR_CONFIRM_4_Full_valid_tool_call_scores_high_confidence()
    {
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp chiến lược Q4", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "họp chiến lược vào thứ năm tuần sau 9h", _anchorTime);

        result.Score.Should().BeGreaterThanOrEqualTo(90);
        result.Level.Should().Be(AiConfidenceLevel.High);
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void FR_CONFIRM_4_Missing_summary_drops_confidence_significantly()
    {
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "", "start": "2026-10-22T09:00:00+07:00"}""");

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "họp lúc 9h", _anchorTime);

        result.Score.Should().BeLessThan(90);
        result.Reasons.Should().Contain(r => r.Contains("summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Ambiguity_keywords_penalize_confidence_score()
    {
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Ăn trưa với đối tác", "start": "2026-10-22T12:00:00+07:00"}""");

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "ăn trưa khoảng thứ năm tuần sau", _anchorTime);

        result.Score.Should().Be(85);
        result.Level.Should().Be(AiConfidenceLevel.Medium);
        result.Reasons.Should().Contain(r => r.Contains("khoảng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Business_validation_errors_drop_score_below_60_percent()
    {
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp", "start": "2026-10-22T10:00:00+07:00", "end": "2026-10-22T09:00:00+07:00"}""");

        var validationResult = AiBusinessValidationResult.WithErrors(
        [
            new AiBusinessValidationMessage(AiBusinessValidationSeverity.Error, "Giờ kết thúc phải sau giờ bắt đầu.")
        ]);

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "họp lúc 10h", _anchorTime, validationResult);

        result.Score.Should().BeLessThan(60);
        result.Level.Should().Be(AiConfidenceLevel.Low);
        result.Reasons.Should().Contain(r => r.Contains("blocking errors", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Invalid_arguments_json_scores_zero_low_confidence()
    {
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, "{malformed json");

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "họp", _anchorTime);

        result.Score.Should().Be(0);
        result.Level.Should().Be(AiConfidenceLevel.Low);
    }

    [Fact]
    public void FR_CONFIRM_4_Null_tool_call_scores_zero_confidence()
    {
        var result = AiConfidenceEvaluator.Evaluate(null, "họp", _anchorTime);

        result.Score.Should().Be(0);
        result.Level.Should().Be(AiConfidenceLevel.Low);
        result.Reasons.Should().ContainSingle(r => r.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Update_and_delete_tool_missing_event_id_penalized()
    {
        var updateCall = new ToolCall(ToolDefinitions.UpdateEvent, """{"summary": "Cập nhật"}""");
        var updateResult = AiConfidenceEvaluator.Evaluate(updateCall, "sửa họp", _anchorTime);
        updateResult.Score.Should().Be(60);
        updateResult.Reasons.Should().Contain(r => r.Contains("event_id", StringComparison.OrdinalIgnoreCase));

        var deleteCall = new ToolCall(ToolDefinitions.DeleteEvent, "{}");
        var deleteResult = AiConfidenceEvaluator.Evaluate(deleteCall, "xoá họp", _anchorTime);
        deleteResult.Score.Should().Be(60);
        deleteResult.Reasons.Should().Contain(r => r.Contains("event_id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Bulk_create_missing_events_penalized()
    {
        var bulkCall = new ToolCall(ToolDefinitions.BulkCreateEvents, """{"events": []}""");
        var result = AiConfidenceEvaluator.Evaluate(bulkCall, "tạo 3 cuộc họp", _anchorTime);
        result.Score.Should().Be(60);
        result.Reasons.Should().Contain(r => r.Contains("events array", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR_CONFIRM_4_Malformed_or_missing_start_time_penalized()
    {
        var missingStart = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Họp"}""");
        var res1 = AiConfidenceEvaluator.Evaluate(missingStart, "họp", _anchorTime);
        res1.Score.Should().Be(70);

        var malformedStart = new ToolCall(ToolDefinitions.CreateEvent, """{"summary": "Họp", "start": "not-a-date"}""");
        var res2 = AiConfidenceEvaluator.Evaluate(malformedStart, "họp", _anchorTime);
        res2.Score.Should().Be(75);
    }

    [Fact]
    public void FR_CONFIRM_4_Validation_warning_penalizes_score_moderately()
    {
        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"summary": "Họp", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""");

        var warnResult = AiBusinessValidationResult.WithWarnings(
        [
            new AiBusinessValidationMessage(AiBusinessValidationSeverity.Warning, "Trùng giờ")
        ]);

        var result = AiConfidenceEvaluator.Evaluate(toolCall, "họp", _anchorTime, warnResult);
        result.Score.Should().Be(85);
        result.Level.Should().Be(AiConfidenceLevel.Medium);
        result.Reasons.Should().Contain(r => r.Contains("warnings", StringComparison.OrdinalIgnoreCase));
    }
}
