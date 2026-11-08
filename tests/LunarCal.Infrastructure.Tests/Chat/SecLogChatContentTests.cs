using System.Net;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Chat;
using LunarCal.Infrastructure.Gemini;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// SEC-LOG-2 (review P4-REVIEW): chat content and raw Gemini responses must not reach the log at the default
/// level (Information). Chat text is Debug-only and the raw response body is never logged.
/// </summary>
public sealed class SecLogChatContentTests
{
    private const string SecretCommandText = "Họp kín với anh Nam về lương thưởng quý 4";
    private const string SecretResponseBody = "RAW-GEMINI-BODY-MARKER-7731";

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<string> Messages { get; } = [];

        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SEC_LOG_2_Worker_does_not_log_command_text_at_information_level()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(7));
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(sink).CreateLogger();

        var command = new AiCommand
        {
            Id = "cmd-1",
            DisplayNo = 1000,
            Text = SecretCommandText,
            TypedAt = now,
            TimeZone = "SE Asia Standard Time",
            InputLang = "vi",
            Status = AiCommandStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        var store = Substitute.For<IAiCommandStore>();
        store.GetPendingQueueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { command });

        var orchestrator = Substitute.For<IChatOrchestrator>();
        orchestrator.ProcessUserMessageAsync(Arg.Any<string>(), Arg.Any<PromptContext>(), Arg.Any<CancellationToken>())
            .Returns(ChatOrchestratorState.Completed);

        var network = Substitute.For<INetworkStatusService>();
        network.IsOnline.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        var worker = new AiCommandQueueWorker(store, orchestrator, network, clock, logger: logger);

        await worker.ProcessPendingBatchAsync(ct);

        sink.Messages.Should().NotBeEmpty();
        sink.Messages.Should().NotContain(m => m.Contains("Nam", StringComparison.Ordinal) || m.Contains("lương", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SEC_LOG_2_Function_calling_probe_failure_does_not_log_the_raw_response_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(sink).CreateLogger();

        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>()).Returns("AIzaSyD_probe_key_5678");
        var settingsStore = Substitute.For<ISettingsStore>();

        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"message\":\"" + SecretResponseBody + "\"}}", Encoding.UTF8, "application/json")
        }));

        var service = new GeminiModelService(httpClient, settingsStore, secretStore, logger: logger);

        var accepted = await service.SetSelectedModelAsync("gemini-any", validateFunctionCalling: true, cancellationToken: ct);

        accepted.Should().BeFalse();
        sink.Messages.Should().NotBeEmpty("the failure is still logged, just without the body");
        sink.Messages.Should().NotContain(m => m.Contains(SecretResponseBody, StringComparison.Ordinal));
    }
}
