using System.Net;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Gemini;
using Microsoft.Extensions.Options;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// P4-R6 (ADR-0016): calendar commands are sent with a low thinking level so the answer comes in seconds, and a
/// rejected thinking field never breaks AI: the call is repeated once without it.
/// </summary>
public sealed class GeminiThinkingConfigTests
{
    private const string OkBody = """
        {"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"},"finishReason":"STOP"}],
         "usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":3,"totalTokenCount":40}}
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GeminiChatModelClient Client(RecordingHandler handler, string? thinkingLevel = "low") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
            Options.Create(new GeminiOptions { DefaultModelId = "gemini-3.8-flash", ThinkingLevel = thinkingLevel }));

    private static ChatRequest Request() => new() { Messages = [ChatMessage.CreateUser("họp 9h mai")] };

    [Fact]
    public async Task FR_CHAT_1_The_request_carries_the_configured_thinking_level_and_the_larger_output_budget()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, OkBody));

        await Client(handler).SendAsync(Request(), TestContext.Current.CancellationToken);

        handler.Bodies.Should().ContainSingle().Which.Should()
            .Contain("\"thinkingConfig\":{\"thinkingLevel\":\"low\"}")
            .And.Contain("\"maxOutputTokens\":4096");
    }

    [Fact]
    public async Task FR_CHAT_1_An_empty_thinking_level_keeps_the_model_default()
    {
        var handler = new RecordingHandler(Json(HttpStatusCode.OK, OkBody));

        await Client(handler, thinkingLevel: "").SendAsync(Request(), TestContext.Current.CancellationToken);

        handler.Bodies.Single().Should().NotContain("thinkingConfig");
    }

    [Fact]
    public async Task FR_CHAT_7_A_400_about_thinking_repeats_the_call_once_without_it_and_remembers()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RecordingHandler(
            Json(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Unknown name \"thinkingLevel\": Cannot find field.","status":"INVALID_ARGUMENT"}}"""),
            Json(HttpStatusCode.OK, OkBody),
            Json(HttpStatusCode.OK, OkBody));
        var client = Client(handler);

        var response = await client.SendAsync(Request(), ct);
        await client.SendAsync(Request(), ct);

        response.Text.Should().Be("ok");
        handler.Bodies.Should().HaveCount(3);
        handler.Bodies[0].Should().Contain("thinkingConfig");
        handler.Bodies[1].Should().NotContain("thinkingConfig", "the retry drops the rejected field");
        handler.Bodies[2].Should().NotContain("thinkingConfig", "later calls do not try it again");
    }

    [Fact]
    public async Task FR_CHAT_7_Other_400_errors_are_not_mistaken_for_a_thinking_problem()
    {
        var handler = new RecordingHandler(
            Json(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT"}}"""));

        var act = async () => await Client(handler).SendAsync(Request(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GeminiApiException>();
        handler.Bodies.Should().ContainSingle();
    }
}
