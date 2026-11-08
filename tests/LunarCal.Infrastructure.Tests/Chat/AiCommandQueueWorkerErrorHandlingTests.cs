using System.Net;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Chat;
using LunarCal.Infrastructure.Gemini;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// FR-CHAT-7, FR-QUEUE-5, ADR-0016 §4: Tests verifying background worker behavior
/// under mapped AI chat error conditions.
/// </summary>
public sealed class AiCommandQueueWorkerErrorHandlingTests
{
    private readonly IAiCommandStore _store = Substitute.For<IAiCommandStore>();
    private readonly IChatOrchestrator _orchestrator = Substitute.For<IChatOrchestrator>();
    private readonly INetworkStatusService _networkStatus = Substitute.For<INetworkStatusService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.FromHours(7));

    public AiCommandQueueWorkerErrorHandlingTests()
    {
        _networkStatus.IsOnline.Returns(true);
        _clock.UtcNow.Returns(_now);
    }

    [Fact]
    public async Task FR_CHAT_7_Worker_stops_retrying_on_invalid_api_key_and_raises_event()
    {
        var command = new AiCommand
        {
            Id = "cmd-invalid-key",
            DisplayNo = 1001,
            Text = "Họp nhóm lúc 9h",
            TypedAt = _now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            RetryCount = 0,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new GeminiApiException(
                GeminiErrorKind.InvalidApiKey,
                "API key not valid",
                statusCode: HttpStatusCode.Unauthorized));

        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        AiChatErrorResult? capturedResult = null;
        worker.AiErrorOccurred += (_, res) => capturedResult = res;

        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([command]);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        capturedResult.Should().NotBeNull();
        capturedResult!.Kind.Should().Be(AiChatErrorKind.InvalidApiKey);
        capturedResult.Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        capturedResult.RequiresSettingsNavigation.Should().BeTrue();
        capturedResult.CanRetry.Should().BeFalse();

        // Must update command status to Error with nextRetryAt = null (no useless retrying)
        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: Arg.Any<string>(),
            retryCount: 0,
            nextRetryAt: null,
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_7_After_the_daily_quota_is_exhausted_the_worker_stops_calling_the_model_for_a_while()
    {
        var command = new AiCommand
        {
            Id = "cmd-quota",
            DisplayNo = 1003,
            Text = "Họp nhóm lúc 9h",
            TypedAt = _now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new GeminiApiException(
                GeminiErrorKind.DailyQuotaExceeded,
                "Daily quota exhausted",
                statusCode: HttpStatusCode.TooManyRequests));
        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([command]);
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);
        _store.ClearReceivedCalls();
        _orchestrator.ClearReceivedCalls();

        // 14 minutes later the queue is still paused: no queue read, no model call.
        _clock.UtcNow.Returns(_now.AddMinutes(14));
        (await worker.ProcessPendingBatchAsync(CancellationToken.None)).Should().Be(0);
        await _store.DidNotReceiveWithAnyArgs().GetPendingQueueAsync(default, default, TestContext.Current.CancellationToken);
        await _orchestrator.DidNotReceiveWithAnyArgs().ProcessUserMessageAsync(default!, default!, TestContext.Current.CancellationToken);

        // After the pause the worker looks at the queue again.
        _clock.UtcNow.Returns(_now.AddMinutes(16));
        await worker.ProcessPendingBatchAsync(CancellationToken.None);
        await _store.ReceivedWithAnyArgs().GetPendingQueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    private AiCommand Cmd(string id, int no) => new()
    {
        Id = id,
        DisplayNo = no,
        Text = "Họp nhóm lúc 9h",
        TypedAt = _now,
        TimeZone = "SE Asia Standard Time",
        InputLang = "vi",
        Status = AiCommandStatus.Pending,
        CreatedAt = _now,
        UpdatedAt = _now,
    };

    [Fact]
    public async Task FR_CHAT_7_A_rate_limit_stops_the_rest_of_the_batch_so_other_commands_do_not_burn_their_retries()
    {
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new GeminiApiException(
                GeminiErrorKind.RateLimitExceeded, "Too many requests per minute", statusCode: HttpStatusCode.TooManyRequests));
        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Cmd("a", 1001), Cmd("b", 1002), Cmd("c", 1003)]);
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        await _orchestrator.ReceivedWithAnyArgs(1).ProcessUserMessageAsync(default!, default!, TestContext.Current.CancellationToken);

        // ...and the queue stays paused for the cool-down.
        _store.ClearReceivedCalls();
        (await worker.ProcessPendingBatchAsync(CancellationToken.None)).Should().Be(0);
        await _store.DidNotReceiveWithAnyArgs().GetPendingQueueAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FR_CHAT_7_The_hourly_call_budget_is_a_hard_stop_whatever_the_queue_contains()
    {
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.Completed);
        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Cmd("a", 1001), Cmd("b", 1002), Cmd("c", 1003), Cmd("d", 1004)]);
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock, new AiQueueWorkerOptions { MaxCallsPerHour = 2 });

        (await worker.ProcessPendingBatchAsync(CancellationToken.None)).Should().Be(2);
        await _orchestrator.ReceivedWithAnyArgs(2).ProcessUserMessageAsync(default!, default!, TestContext.Current.CancellationToken);

        // Still inside the hour: nothing more is sent.
        _clock.UtcNow.Returns(_now.AddMinutes(59));
        (await worker.ProcessPendingBatchAsync(CancellationToken.None)).Should().Be(0);

        // An hour after the first call the budget frees up again.
        _clock.UtcNow.Returns(_now.AddMinutes(61));
        (await worker.ProcessPendingBatchAsync(CancellationToken.None)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FR_QUEUE_3_A_network_failure_while_online_waits_before_the_next_try_instead_of_hammering()
    {
        var command = Cmd("net", 1001);
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new HttpRequestException("The SSL connection could not be established"));
        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([command]);
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Pending,
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Is<DateTimeOffset?>(t => t == _now + TimeSpan.FromSeconds(30)),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_QUEUE_5_The_worker_loop_asks_the_queue_for_one_command_at_a_time()
    {
        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        await worker.ProcessPendingBatchAsync(1, CancellationToken.None);

        await _store.Received(1).GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_7_Worker_sets_cooldown_delay_on_rate_limit_429()
    {
        var command = new AiCommand
        {
            Id = "cmd-rate-limit",
            DisplayNo = 1002,
            Text = "Tạo lịch ngày mai",
            TypedAt = _now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            RetryCount = 1,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new GeminiApiException(
                GeminiErrorKind.RateLimitExceeded,
                "Rate limit exceeded (429)",
                statusCode: (HttpStatusCode)429));

        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        AiChatErrorResult? capturedResult = null;
        worker.AiErrorOccurred += (_, res) => capturedResult = res;

        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([command]);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        capturedResult.Should().NotBeNull();
        capturedResult!.Kind.Should().Be(AiChatErrorKind.RateLimitExceeded);
        capturedResult.CanRetry.Should().BeTrue();

        // nextRetryAt must be in the future (respecting rate limit cooldown)
        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: Arg.Any<string>(),
            retryCount: 2,
            nextRetryAt: Arg.Is<DateTimeOffset?>(dt => dt.HasValue && dt.Value > _now),
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_7_Worker_stops_retrying_on_safety_blocked()
    {
        var command = new AiCommand
        {
            Id = "cmd-safety",
            DisplayNo = 1003,
            Text = "Lệnh nhạy cảm",
            TypedAt = _now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            RetryCount = 0,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new GeminiApiException(
                GeminiErrorKind.SafetyBlocked,
                "Candidate was blocked due to SAFETY",
                geminiStatus: "BLOCKED_BY_SAFETY"));

        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([command]);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Error,
            lastError: Arg.Any<string>(),
            retryCount: 0,
            nextRetryAt: null,
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_CHAT_7_Worker_preserves_command_in_pending_when_network_drops()
    {
        var command = new AiCommand
        {
            Id = "cmd-offline",
            DisplayNo = 1004,
            Text = "Nhắc tôi tập thể dục",
            TypedAt = _now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            RetryCount = 0,
            CreatedAt = _now,
            UpdatedAt = _now
        };

        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new HttpRequestException(
                "No such host is known. (generativelanguage.googleapis.com:443)"));

        var worker = new AiCommandQueueWorker(_store, _orchestrator, _networkStatus, _clock);

        _store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([command]);

        await worker.ProcessPendingBatchAsync(CancellationToken.None);

        // Command is restored to Pending with a 30 s backoff (never an immediate retry) so it waits for the network to recover
        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Pending,
            lastError: Arg.Any<string>(),
            nextRetryAt: _now + TimeSpan.FromSeconds(30),
            retryCount: Arg.Any<int?>(),
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
