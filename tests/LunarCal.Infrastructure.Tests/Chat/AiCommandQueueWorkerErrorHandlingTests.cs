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

        // Command is restored to Pending with nextRetryAt = null so it waits for network restoration
        await _store.Received(1).UpdateStatusAsync(
            command.Id,
            AiCommandStatus.Pending,
            lastError: Arg.Any<string>(),
            nextRetryAt: null,
            retryCount: Arg.Any<int?>(),
            batchId: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
