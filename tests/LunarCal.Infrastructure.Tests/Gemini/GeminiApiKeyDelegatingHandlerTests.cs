using System.Net;
using System.Security;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiApiKeyDelegatingHandler"/> (ADR-0016 §2, SEC-KEY-2, SEC-KEY-3).
/// </summary>
[Collection("SecretValueRegistry")]
public sealed class GeminiApiKeyDelegatingHandlerTests : IDisposable
{
    public GeminiApiKeyDelegatingHandlerTests()
    {
        SecretValueRegistry.Clear();
    }

    public void Dispose()
    {
        SecretValueRegistry.Clear();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(Response);
        }
    }

    [Fact]
    public async Task SEC_KEY_2_Handler_attaches_key_to_header_and_passes_to_gemini()
    {
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns("custom_test_gemini_api_key_12345");

        var inner = new CaptureHandler();
        var handler = new GeminiApiKeyDelegatingHandler(secretStore)
        {
            InnerHandler = inner
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CapturedRequest.Should().NotBeNull();
        inner.CapturedRequest!.Headers.Contains("x-goog-api-key").Should().BeTrue();
        inner.CapturedRequest.Headers.GetValues("x-goog-api-key").First().Should().Be("custom_test_gemini_api_key_12345");
    }

    [Fact]
    public async Task SEC_KEY_2_Handler_rejects_requests_with_key_in_query_string()
    {
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns("some_key_12345");

        var inner = new CaptureHandler();
        var handler = new GeminiApiKeyDelegatingHandler(secretStore)
        {
            InnerHandler = inner
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=leaked_query_key");

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*query string*");
    }

    [Fact]
    public async Task SEC_KEY_2_Handler_rejects_requests_to_non_gemini_host()
    {
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns("some_key_12345");

        var inner = new CaptureHandler();
        var handler = new GeminiApiKeyDelegatingHandler(secretStore)
        {
            InnerHandler = inner
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://evil-proxy.com/v1beta/models/gemini-3.8-flash:generateContent");

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*generativelanguage.googleapis.com*");
    }

    [Fact]
    public async Task SEC_KEY_3_Handler_throws_MissingApiKey_when_key_is_not_in_store()
    {
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var inner = new CaptureHandler();
        var handler = new GeminiApiKeyDelegatingHandler(secretStore)
        {
            InnerHandler = inner
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.MissingApiKey);
    }

    [Fact]
    public async Task ADR_0016_Handler_registers_key_in_SecretValueRegistry()
    {
        var testKey = "unique_secret_gemini_key_for_registry_test";
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(testKey);

        var inner = new CaptureHandler();
        var handler = new GeminiApiKeyDelegatingHandler(secretStore)
        {
            InnerHandler = inner
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        var redacted = LogRedactor.Redact($"Log output contains {testKey}");
        redacted.Should().NotContain(testKey);
        redacted.Should().Contain(LogRedactor.RedactedApiKey);
    }
}
