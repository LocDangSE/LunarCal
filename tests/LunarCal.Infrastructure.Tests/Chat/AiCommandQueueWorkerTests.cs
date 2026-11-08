using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="AiCommandQueueWorker"/> (FR-QUEUE-1..9, FR-CONFIRM-8).
/// Verifies state transitions, retry policy, crash recovery, and offline handling.
/// </summary>
public sealed class AiCommandQueueWorkerTests
{
    private readonly IAiCommandStore _commandStore = Substitute.For<IAiCommandStore>();
    private readonly IChatOrchestrator _orchestrator = Substitute.For<IChatOrchestrator>();
    private readonly INetworkStatusService _networkStatus = Substitute.For<INetworkStatusService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DateTimeOffset _testNow = new(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(7));

    public AiCommandQueueWorkerTests()
    {
        _clock.UtcNow.Returns(_testNow);
        _networkStatus.IsOnline.Returns(true);
    }

    private AiCommand CreateTestCommand(
        string id = "cmd-1",
        int displayNo = 1000,
        string status = AiCommandStatus.Pending,
        int retryCount = 0,
        string text = "Tạo cuộc họp ngày mai 9h")
    {
        return new AiCommand
        {
            Id = id,
            DisplayNo = displayNo,
            Text = text,
            TypedAt = _testNow,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = status,
            RetryCount = retryCount,
            NextRetryAt = null,
            LastError = null,
            BatchId = null,
            CreatedAt = _testNow,
            UpdatedAt = _testNow
        };
    }

    [Fact]
    public async Task FR_QUEUE_6_A_command_cancelled_while_the_model_works_is_not_brought_back_to_needs_confirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CreateTestCommand();
        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });
        _orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.AwaitingConfirmation);
        _orchestrator.PendingToolCall.Returns(new ToolCall(ToolDefinitions.CreateEvent, "{}"));

        // The user pressed "Huỷ" while the model was still working.
        _commandStore.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(CreateTestCommand(status: AiCommandStatus.Cancelled));

        var worker = new AiCommandQueueWorker(_commandStore, _orchestrator, _networkStatus, _clock);

        await worker.ProcessPendingBatchAsync(ct);

        await _commandStore.DidNotReceive().SetProposalAsync(command.Id, Arg.Any<AiCommandProposal?>(), Arg.Any<CancellationToken>());
        await _commandStore.DidNotReceive().UpdateStatusAsync(
            command.Id,
            AiCommandStatus.NeedsConfirmation,
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_2_Worker_processes_pending_command_and_completes()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CreateTestCommand();

        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });

        _orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.Completed);

        _orchestrator.ExecutionResult.Returns(ToolExecutionResult.Success(batchId: "batch-456"));

        var worker = new AiCommandQueueWorker(_commandStore, _orchestrator, _networkStatus, _clock);

        // Act
        var processed = await worker.ProcessPendingBatchAsync(ct);

        // Assert
        processed.Should().Be(1);

        // State progression: pending -> processing -> completed
        await _commandStore.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Processing,
            cancellationToken: Arg.Any<CancellationToken>());

        await _commandStore.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Completed,
            lastError: Arg.Any<string?>(),
            retryCount: Arg.Any<int?>(),
            nextRetryAt: Arg.Any<DateTimeOffset?>(),
            batchId: "batch-456",
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_7_Destructive_or_unconfirmed_action_stops_at_needs_confirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CreateTestCommand(text: "Xoá sự kiện họp");

        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });

        // Orchestrator pauses at AwaitingConfirmation (FR-CHAT-2, SEC-PI-2)
        _orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.AwaitingConfirmation);

        var worker = new AiCommandQueueWorker(_commandStore, _orchestrator, _networkStatus, _clock);

        // Act
        var processed = await worker.ProcessPendingBatchAsync(ct);

        // Assert: Worker never auto-executes; sets status to needs_confirmation
        processed.Should().Be(1);

        await _commandStore.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.NeedsConfirmation,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CONFIRM_8_Clarification_question_stops_at_needs_confirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CreateTestCommand(text: "Họp tuần sau");

        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });

        _orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.AwaitingClarification);

        _orchestrator.ClarificationQuestion.Returns("Bạn muốn họp vào thứ mấy lúc mấy giờ?");

        var worker = new AiCommandQueueWorker(_commandStore, _orchestrator, _networkStatus, _clock);

        // Act
        var processed = await worker.ProcessPendingBatchAsync(ct);

        // Assert
        processed.Should().Be(1);

        await _commandStore.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.NeedsConfirmation,
            lastError: "Bạn muốn họp vào thứ mấy lúc mấy giờ?",
            retryCount: Arg.Any<int?>(),
            nextRetryAt: Arg.Any<DateTimeOffset?>(),
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_5_Failure_schedules_exponential_backoff_up_to_5_retries()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CreateTestCommand(retryCount: 0);

        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });

        _orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.Failed);

        _orchestrator.ErrorMessage.Returns("Gemini API 503 Service Unavailable");

        var retryPolicy = new AiQueueRetryPolicy(baseDelay: TimeSpan.FromMinutes(1), seed: 42);
        var worker = new AiCommandQueueWorker(
            _commandStore,
            _orchestrator,
            _networkStatus,
            _clock,
            retryPolicy: retryPolicy);

        // Act 1: 1st failure (retry count 0 -> 1)
        await worker.ProcessPendingBatchAsync(ct);

        // Assert 1: retryCount becomes 1, nextRetryAt is scheduled
        await _commandStore.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: "Gemini API 503 Service Unavailable",
            retryCount: 1,
            nextRetryAt: Arg.Is<DateTimeOffset?>(dt => dt.HasValue && dt.Value > _testNow),
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());

        // Act 2: 5th failure (retry count 4 -> 5, reaches max 5 retries)
        var commandAttempt4 = CreateTestCommand(retryCount: 4);
        _commandStore.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { commandAttempt4 });

        await worker.ProcessPendingBatchAsync(ct);

        // Assert 2: retryCount reaches 5, nextRetryAt is null (unretryable error, manual intervention required)
        await _commandStore.Received(1).UpdateStatusAsync(
            commandAttempt4.Id,
            AiCommandStatus.Error,
            lastError: "Gemini API 503 Service Unavailable",
            retryCount: 5,
            nextRetryAt: null,
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FR_QUEUE_8_Idempotency_request_id_derived_deterministically()
    {
        var command = CreateTestCommand(id: "abc-xyz-123");

        command.RequestId.Should().Be("lunarcal-ai-abc-xyz-123");
        // Must stay identical across calls to prevent duplicate creation on retries
        command.RequestId.Should().Be("lunarcal-ai-abc-xyz-123");
    }

    [Fact]
    public async Task FR_QUEUE_8_Startup_recovers_crashed_commands_and_cleans_old()
    {
        var ct = TestContext.Current.CancellationToken;

        _commandStore.RecoverProcessingCommandsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(2);

        _commandStore.CleanupOldCommandsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(5);

        var worker = new AiCommandQueueWorker(_commandStore, _orchestrator, _networkStatus, _clock);

        // Act
        await worker.InitializeStartupAsync(ct);

        // Assert
        await _commandStore.Received(1).RecoverProcessingCommandsAsync(_testNow, Arg.Any<CancellationToken>());
        await _commandStore.Received(1).CleanupOldCommandsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
