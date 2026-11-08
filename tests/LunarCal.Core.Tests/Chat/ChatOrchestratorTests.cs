using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="ChatOrchestrator"/> (FR-CHAT-1, FR-CHAT-2, SEC-PI-2, FR-CONFIRM-8).
/// Verifies the state machine, security boundaries, user confirmation gates, and tool execution.
/// </summary>
public sealed class ChatOrchestratorTests
{
    private static readonly string[] _testCalendars = ["Work", "Personal"];
    private static readonly string[] _testEventIds = ["evt-1"];

    private readonly IChatModelClient _chatModelClient = Substitute.For<IChatModelClient>();
    private readonly ISystemPromptProvider _promptProvider = Substitute.For<ISystemPromptProvider>();
    private readonly IToolExecutor _toolExecutor = Substitute.For<IToolExecutor>();
    private readonly PromptContext _testContext;

    public ChatOrchestratorTests()
    {
        _testContext = new PromptContext(
            TypedAt: new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(7)),
            TimeZoneId: "SE Asia Standard Time",
            Locale: "vi-VN",
            AvailableCalendars: _testCalendars);

        _promptProvider.RenderSystemPrompt(Arg.Any<PromptContext>(), Arg.Any<string?>())
            .Returns("Rendered system prompt");

        _toolExecutor.ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Success(outputMessage: "Created successfully", eventIds: _testEventIds));
    }

    [Fact]
    public async Task FR_CHAT_2_Never_writes_without_confirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Team Meeting","start":"2026-10-17T09:00:00+07:00","end":"2026-10-17T10:00:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { toolCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Họp nhóm 9h sáng mai", _testContext, ct);

        // Assert: must be in AwaitingConfirmation, and executor must NOT have been called!
        state.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        orchestrator.State.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        orchestrator.PendingToolCall.Should().NotBeNull();
        orchestrator.PendingToolCall!.Name.Should().Be(ToolDefinitions.CreateEvent);

        // SEC-PI-2 / FR-CHAT-2: Zero execution before user confirmation
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ToolDefinitions.DeleteEvent, """{"eventId":"evt-123","scope":"this"}""")]
    [InlineData(ToolDefinitions.BulkCreateEvents, """{"events":[{"title":"E1","start":"2026-10-17T09:00:00+07:00"}]}""")]
    [InlineData(ToolDefinitions.UpdateEvent, """{"eventId":"evt-123","title":"New Title"}""")]
    [InlineData(ToolDefinitions.CreateRecurringEvent, """{"title":"Weekly","start":"2026-10-17T09:00:00+07:00","rrule":"FREQ=WEEKLY"}""")]
    [InlineData(ToolDefinitions.CreateLunarRecurringEvent, """{"title":"Rằm","lunarDay":15}""")]
    public async Task FR_CHAT_2_Never_deletes_or_bulk_writes_without_confirmation_even_when_auto_confirm_is_true(
        string toolName,
        string argsJson)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new ChatOrchestratorOptions { AutoConfirmSingleEvent = true };
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor, options);

        var toolCall = new ToolCall(toolName, argsJson);

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { toolCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Thực hiện lệnh", _testContext, ct);

        // Assert: MUST wait for confirmation! AutoConfirmSingleEvent does not apply.
        state.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_2_AutoConfirmSingleEvent_when_enabled_executes_create_event_immediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new ChatOrchestratorOptions { AutoConfirmSingleEvent = true };
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor, options);

        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Coffee Chat","start":"2026-10-17T14:00:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { toolCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Đi uống cafe lúc 14h ngày mai", _testContext, ct);

        // Assert
        state.Should().Be(ChatOrchestratorState.Completed);
        orchestrator.ExecutionResult.Should().NotBeNull();
        orchestrator.ExecutionResult!.IsSuccess.Should().BeTrue();

        await _toolExecutor.Received(1).ExecuteAsync(
            Arg.Is<ToolCall>(c => c.Name == ToolDefinitions.CreateEvent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_1_Full_cycle_user_confirms_executes_tool()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var toolCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Doctor Appointment","start":"2026-10-18T08:30:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { toolCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        // Step 1: User enters message -> AwaitingConfirmation
        var step1 = await orchestrator.ProcessUserMessageAsync("Khám bác sĩ 8h30 ngày kia", _testContext, ct);
        step1.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());

        // Step 2: User confirms -> Executing -> Completed
        var step2 = await orchestrator.ConfirmAsync(cancellationToken: ct);
        step2.Should().Be(ChatOrchestratorState.Completed);

        await _toolExecutor.Received(1).ExecuteAsync(
            Arg.Is<ToolCall>(c => c.Name == ToolDefinitions.CreateEvent),
            Arg.Any<CancellationToken>());

        orchestrator.ExecutionResult!.IsSuccess.Should().BeTrue();
        orchestrator.ConversationHistory.Should().Contain(m => m.Role == ChatMessageRole.Tool);
    }

    [Fact]
    public async Task FR_CONFIRM_6_User_edits_tool_call_before_confirming()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var originalCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Meeting","start":"2026-10-18T09:00:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { originalCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);
        orchestrator.State.Should().Be(ChatOrchestratorState.AwaitingConfirmation);

        // Act: User edits title in UI
        var modifiedCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Team Retrospective","start":"2026-10-18T09:00:00+07:00"}""");

        var result = await orchestrator.ConfirmAsync(modifiedCall, ct);

        // Assert
        result.Should().Be(ChatOrchestratorState.Completed);
        await _toolExecutor.Received(1).ExecuteAsync(
            Arg.Is<ToolCall>(c => c.ArgumentsJson.Contains("Team Retrospective")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CONFIRM_6_Modified_call_with_invalid_arguments_remains_in_AwaitingConfirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var originalCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Meeting","start":"2026-10-18T09:00:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse { ToolCalls = new[] { originalCall } });

        await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);

        // Act: User edits tool call into invalid schema (e.g. empty title)
        var invalidCall = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"","start":"2026-10-18T09:00:00+07:00"}""");
        var result = await orchestrator.ConfirmAsync(invalidCall, ct);

        // Assert: does not execute, stays in AwaitingConfirmation with error
        result.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        orchestrator.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CONFIRM_8_Model_returns_clarification_question_transitions_to_AwaitingClarification()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        // Turn 1: Model asks for missing start time
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                Text = "Bạn muốn đặt lịch họp ngày mai vào lúc mấy giờ?",
                FinishReason = ChatFinishReason.Stop
            });

        // Act 1
        var state1 = await orchestrator.ProcessUserMessageAsync("Nhắc tôi họp ngày mai", _testContext, ct);

        // Assert 1
        state1.Should().Be(ChatOrchestratorState.AwaitingClarification);
        orchestrator.ClarificationQuestion.Should().Be("Bạn muốn đặt lịch họp ngày mai vào lúc mấy giờ?");
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());

        // Turn 2: User answers clarification -> model generates tool call
        var resolvedCall = new ToolCall(
            ToolDefinitions.CreateEvent,
            """{"title":"Họp","start":"2026-10-17T10:00:00+07:00"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { resolvedCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        // Act 2
        var state2 = await orchestrator.AnswerClarificationAsync("Lúc 10h sáng", _testContext, ct);

        // Assert 2
        state2.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        orchestrator.PendingToolCall.Should().NotBeNull();
        orchestrator.ClarificationQuestion.Should().BeNull();
    }

    [Fact]
    public async Task FR_CONFIRM_8_Clarification_preserves_provider_state_for_gemini_thought_signature()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        // Turn 1: Model asks clarification and returns ProviderState with opaque thoughtSignature
        const string mockThoughtSignature = "{\"thoughtSignature\":\"opaque_gemini_token_abc123\"}";
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                Text = "Mấy giờ bạn họp?",
                FinishReason = ChatFinishReason.Stop,
                ProviderState = mockThoughtSignature
            });

        var state1 = await orchestrator.ProcessUserMessageAsync("Nhắc họp ngày mai", _testContext, ct);
        state1.Should().Be(ChatOrchestratorState.AwaitingClarification);

        // Turn 2: User answers. Assert that ChatRequest passed to client preserves ProviderState
        ChatRequest? secondRequest = null;
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, "{\"title\":\"Họp\",\"start\":\"2026-10-17T14:00:00+07:00\"}");

        _chatModelClient.SendAsync(Arg.Do<ChatRequest>(r => secondRequest = r), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { toolCall },
                FinishReason = ChatFinishReason.ToolCalls
            });

        var state2 = await orchestrator.AnswerClarificationAsync("Lúc 14h", _testContext, ct);

        state2.Should().Be(ChatOrchestratorState.AwaitingConfirmation);
        secondRequest.Should().NotBeNull();
        secondRequest!.ProviderState.Should().Be(mockThoughtSignature);
    }

    [Fact]
    public async Task SEC_PI_2_Invalid_tool_call_fails_validation_and_never_executes()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var maliciousCall = new ToolCall(
            ToolDefinitions.DeleteEvent,
            """{"eventId":"*","scope":"all"}""");

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                ToolCalls = new[] { maliciousCall }
            });

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Delete everything", _testContext, ct);

        // Assert
        state.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("Wildcard");
        orchestrator.PendingToolCall.Should().BeNull();
        await _toolExecutor.DidNotReceive().ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SEC_PI_2_Model_safety_block_transitions_to_Failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse
            {
                FinishReason = ChatFinishReason.Safety
            });

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Unsafe prompt", _testContext, ct);

        // Assert
        state.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("safety");
    }

    [Fact]
    public async Task FR_CHAT_3_Session_tool_call_limit_exceeded_blocks_execution()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new ChatOrchestratorOptions { MaxToolCallsPerSession = 2 };
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor, options);

        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"E","start":"2026-10-17T09:00:00+07:00"}""");
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse { ToolCalls = new[] { toolCall } });

        // Call 1
        await orchestrator.ProcessUserMessageAsync("Message 1", _testContext, ct);
        orchestrator.SessionToolCallCount.Should().Be(1);

        // Call 2
        await orchestrator.ProcessUserMessageAsync("Message 2", _testContext, ct);
        orchestrator.SessionToolCallCount.Should().Be(2);

        // Call 3: should be blocked
        var state3 = await orchestrator.ProcessUserMessageAsync("Message 3", _testContext, ct);
        state3.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("Session tool call limit");
    }

    [Fact]
    public async Task Cancel_transitions_to_Cancelled_and_clears_pending_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Meeting","start":"2026-10-17T09:00:00+07:00"}""");
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse { ToolCalls = new[] { toolCall } });

        await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);
        orchestrator.State.Should().Be(ChatOrchestratorState.AwaitingConfirmation);

        // Act
        orchestrator.Cancel();

        // Assert
        orchestrator.State.Should().Be(ChatOrchestratorState.Cancelled);
        orchestrator.PendingToolCall.Should().BeNull();
    }

    [Fact]
    public async Task Reset_restores_idle_state_and_clears_conversation_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Meeting","start":"2026-10-17T09:00:00+07:00"}""");
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse { ToolCalls = new[] { toolCall } });

        await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);
        orchestrator.ConversationHistory.Should().NotBeEmpty();

        // Act
        orchestrator.Reset();

        // Assert
        orchestrator.State.Should().Be(ChatOrchestratorState.Idle);
        orchestrator.PendingToolCall.Should().BeNull();
        orchestrator.ConversationHistory.Should().BeEmpty();
        orchestrator.SessionToolCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ModelClient_exception_transitions_to_Failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network failure"));

        // Act
        var state = await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);

        // Assert
        state.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("Network failure");
    }

    [Fact]
    public async Task ToolExecutor_exception_transitions_to_Failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        var toolCall = new ToolCall(ToolDefinitions.CreateEvent, """{"title":"Meeting","start":"2026-10-17T09:00:00+07:00"}""");
        _chatModelClient.SendAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse { ToolCalls = new[] { toolCall } });

        _toolExecutor.ExecuteAsync(Arg.Any<ToolCall>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Calendar DB locked"));

        await orchestrator.ProcessUserMessageAsync("Họp", _testContext, ct);

        // Act
        var state = await orchestrator.ConfirmAsync(cancellationToken: ct);

        // Assert
        state.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("Calendar DB locked");
    }

    [Fact]
    public async Task Empty_or_too_long_user_message_fails_fast()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);

        var emptyState = await orchestrator.ProcessUserMessageAsync("   ", _testContext, ct);
        emptyState.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("cannot be empty");

        var longMessage = new string('A', ToolDefinitions.MaxStringLength + 1);
        var longState = await orchestrator.ProcessUserMessageAsync(longMessage, _testContext, ct);
        longState.Should().Be(ChatOrchestratorState.Failed);
        orchestrator.ErrorMessage.Should().Contain("exceeds max length");
    }

    [Fact]
    public async Task Confirm_when_not_awaiting_confirmation_throws_InvalidOperationException()
    {
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        var action = async () => await orchestrator.ConfirmAsync(cancellationToken: TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AnswerClarification_when_not_awaiting_clarification_throws_InvalidOperationException()
    {
        var orchestrator = new ChatOrchestrator(_chatModelClient, _promptProvider, _toolExecutor);
        var action = async () => await orchestrator.AnswerClarificationAsync("Answer", _testContext, cancellationToken: TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
}
