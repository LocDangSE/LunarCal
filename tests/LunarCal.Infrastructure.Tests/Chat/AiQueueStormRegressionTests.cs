using System.Net;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Chat;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// P4-R9/P4-R10: the incident of 2026-11-08, replayed against the real SQLite queue. A command that failed once with a
/// transient error and then permanently (daily quota) was sent to Gemini thousands of times. Whatever the failure
/// pattern, the number of model calls must stay tiny.
/// </summary>
public sealed class AiQueueStormRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LunarCal_Storm_" + Guid.NewGuid().ToString("N"));
    private readonly DpapiSecretStore _secrets;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteAiCommandStore _store;
    private readonly IChatOrchestrator _orchestrator = Substitute.For<IChatOrchestrator>();
    private readonly INetworkStatusService _network = Substitute.For<INetworkStatusService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public AiQueueStormRegressionTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "secrets"));
        _secrets = new DpapiSecretStore(Path.Combine(_directory, "secrets"));
        _factory = new SqliteConnectionFactory(_secrets, new SqliteStorageOptions { DatabasePath = Path.Combine(_directory, "storm.db") });
        _store = new SqliteAiCommandStore(_factory);
        _network.IsOnline.Returns(true);
        _clock.UtcNow.Returns(_ => _now);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _secrets.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test data: a locked file is cleaned up with the temp folder later.
        }
    }

    private static GeminiApiException Failure(GeminiErrorKind kind) => new(kind, kind.ToString(), statusCode: HttpStatusCode.TooManyRequests);

    private async Task<int> RunPassesAsync(AiCommandQueueWorker worker, int passes, TimeSpan step, CancellationToken ct)
    {
        var processed = 0;
        for (var i = 0; i < passes; i++)
        {
            processed += await worker.ProcessPendingBatchAsync(1, ct);
            _now += step;
        }

        return processed;
    }

    [Fact]
    public async Task FR_QUEUE_5_A_transient_failure_followed_by_a_permanent_one_costs_two_model_calls_not_thousands()
    {
        var ct = TestContext.Current.CancellationToken;
        await _factory.EnsureInitializedAsync(ct);
        await _store.EnqueueAsync("tạo lịch học thạc sĩ", _now, "SE Asia Standard Time", "vi", ct);

        var errors = new Queue<GeminiErrorKind>([GeminiErrorKind.TransientError, GeminiErrorKind.DailyQuotaExceeded]);
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw Failure(errors.Count > 0 ? errors.Dequeue() : GeminiErrorKind.DailyQuotaExceeded));
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _network, _clock);

        // 500 passes, 20 minutes apart (long enough to outlast every pause): the command may be tried only twice.
        await RunPassesAsync(worker, 500, TimeSpan.FromMinutes(20), ct);

        await _orchestrator.ReceivedWithAnyArgs(2).ProcessUserMessageAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FR_QUEUE_5_Any_failure_pattern_is_capped_by_the_retry_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await _factory.EnsureInitializedAsync(ct);
        await _store.EnqueueAsync("họp 9h", _now, "SE Asia Standard Time", "vi", ct);
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw Failure(GeminiErrorKind.TransientError));
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _network, _clock);

        // A 503 forever: first attempt plus five retries at most, never more (FR-QUEUE-5).
        await RunPassesAsync(worker, 400, TimeSpan.FromMinutes(30), ct);

        await _orchestrator.ReceivedWithAnyArgs(5).ProcessUserMessageAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FR_QUEUE_3_A_network_that_looks_online_but_always_fails_is_tried_at_most_every_30_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        await _factory.EnsureInitializedAsync(ct);
        await _store.EnqueueAsync("họp 9h", _now, "SE Asia Standard Time", "vi", ct);
        _orchestrator
            .ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatOrchestratorState>>(_ => throw new HttpRequestException("The SSL connection could not be established"));
        var worker = new AiCommandQueueWorker(_store, _orchestrator, _network, _clock);

        // 10 minutes of 5-second polling is 120 passes: with the 30 s backoff that is about 20 calls, not 120.
        await RunPassesAsync(worker, 120, TimeSpan.FromSeconds(5), ct);

        var calls = _orchestrator.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatOrchestrator.ProcessUserMessageAsync));
        calls.Should().BeInRange(18, 22);
    }
}
