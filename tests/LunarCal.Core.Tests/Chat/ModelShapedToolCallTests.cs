using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

/// <summary>
/// P4-REVIEW: the validator, confidence heuristic and confirmation dialog must understand a tool call shaped by the
/// real schema in <see cref="ToolDefinitions"/> (<c>title</c>, <c>allDay</c>, <c>eventId</c>), not only the legacy
/// <c>summary</c> spelling the first tests were written against.
/// </summary>
public sealed class ModelShapedToolCallTests
{
    private static readonly DateTimeOffset _typedAt = new(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7));

    private const string ModelCreateEvent =
        """{"title": "Họp chiến lược Q4", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00"}""";

    [Fact]
    public async Task FR_CONFIRM_5_Create_event_built_from_schema_field_title_passes_business_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventStore = Substitute.For<IEventStore>();
        eventStore.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await new AiBusinessValidator().ValidateAsync(
            new ToolCall(ToolDefinitions.CreateEvent, ModelCreateEvent), eventStore, _typedAt, cancellationToken: ct);

        result.HasError.Should().BeFalse("a model call carries 'title', and that must not be reported as an empty title");
    }

    [Fact]
    public void FR_CONFIRM_4_Create_event_built_from_schema_field_title_scores_high_confidence()
    {
        var result = AiConfidenceEvaluator.Evaluate(
            new ToolCall(ToolDefinitions.CreateEvent, ModelCreateEvent), "họp chiến lược vào thứ năm tuần sau 9h", _typedAt);

        result.Level.Should().Be(AiConfidenceLevel.High);
        result.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void FR_CONFIRM_4_Update_and_delete_accept_schema_field_eventId()
    {
        AiConfidenceEvaluator.Evaluate(new ToolCall(ToolDefinitions.DeleteEvent, """{"eventId": "ev-1"}"""), null, _typedAt)
            .Reasons.Should().BeEmpty();
        AiConfidenceEvaluator.Evaluate(new ToolCall(ToolDefinitions.UpdateEvent, """{"eventId": "ev-1", "title": "Đổi tên"}"""), null, _typedAt)
            .Reasons.Should().BeEmpty();
    }
}
