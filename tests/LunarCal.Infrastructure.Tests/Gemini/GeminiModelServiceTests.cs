using System.Net;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Gemini;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiModelService"/> (FR-BYOK-5, ADR-0016 §2).
/// Verifies model listing, function calling probe, and model selection persistence in ISettingsStore.
/// </summary>
public sealed class GeminiModelServiceTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task FR_BYOK_5_GetAvailableModelsAsync_queries_api_and_returns_filtered_models()
    {
        var ct = TestContext.Current.CancellationToken;
        const string fakeApiKey = "AIzaSyD_my_valid_test_key_1234";

        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(fakeApiKey);

        var settingsStore = Substitute.For<ISettingsStore>();

        var json = """
        {
          "models": [
            {
              "name": "models/gemini-3.8-flash",
              "displayName": "Gemini 3.8 Flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-flash-latest",
              "displayName": "Latest Flash",
              "supportedGenerationMethods": ["generateContent"]
            }
          ]
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.ToString().Should().Be("https://generativelanguage.googleapis.com/v1beta/models");
            req.Headers.GetValues("x-goog-api-key").Single().Should().Be(fakeApiKey);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var service = new GeminiModelService(httpClient, settingsStore, secretStore);

        var models = await service.GetAvailableModelsAsync(ct);

        models.Should().HaveCount(1);
        models[0].Id.Should().Be("gemini-3.8-flash");
    }

    [Fact]
    public async Task FR_BYOK_5_GetAvailableModelsAsync_returns_fallbacks_when_no_api_key_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var settingsStore = Substitute.For<ISettingsStore>();
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var service = new GeminiModelService(httpClient, settingsStore, secretStore);

        var models = await service.GetAvailableModelsAsync(ct);

        models.Should().NotBeEmpty();
        models.Select(m => m.Id).Should().Contain("gemini-3.8-flash");
    }

    [Fact]
    public async Task FR_BYOK_5_GetSelectedModelIdAsync_reads_settings_store_or_falls_back_to_options()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var settingsStore = Substitute.For<ISettingsStore>();
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var options = Options.Create(new GeminiOptions { DefaultModelId = "gemini-3.8-flash" });
        var service = new GeminiModelService(httpClient, settingsStore, secretStore, options);

        // Case 1: SettingsStore has custom model
        settingsStore.GetStringAsync(SettingKeys.GeminiModelId, Arg.Any<CancellationToken>())
            .Returns("gemini-3.5-flash-lite");

        var selected = await service.GetSelectedModelIdAsync(ct);
        selected.Should().Be("gemini-3.5-flash-lite");

        // Case 2: SettingsStore has empty -> fallback to DefaultModelId
        settingsStore.GetStringAsync(SettingKeys.GeminiModelId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var fallback = await service.GetSelectedModelIdAsync(ct);
        fallback.Should().Be("gemini-3.8-flash");
    }

    [Fact]
    public async Task FR_BYOK_5_SetSelectedModelAsync_validates_function_calling_and_persists_on_success()
    {
        var ct = TestContext.Current.CancellationToken;
        const string fakeApiKey = "AIzaSyD_probe_key_5678";
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(fakeApiKey);

        var settingsStore = Substitute.For<ISettingsStore>();

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.ToString().Should().Contain("gemini-3.6-flash:generateContent");
            req.Headers.GetValues("x-goog-api-key").Single().Should().Be(fakeApiKey);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var service = new GeminiModelService(httpClient, settingsStore, secretStore);

        var result = await service.SetSelectedModelAsync("models/gemini-3.6-flash", validateFunctionCalling: true, cancellationToken: ct);

        result.Should().BeTrue();
        await settingsStore.Received(1).SetStringAsync(SettingKeys.GeminiModelId, "gemini-3.6-flash", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_BYOK_5_SetSelectedModelAsync_rejects_model_when_function_calling_validation_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        const string fakeApiKey = "AIzaSyD_probe_key_5678";
        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(fakeApiKey);

        var settingsStore = Substitute.For<ISettingsStore>();

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"Function calling is not supported for this model."}}""", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var service = new GeminiModelService(httpClient, settingsStore, secretStore);

        var result = await service.SetSelectedModelAsync("gemini-unsupported-model", validateFunctionCalling: true, cancellationToken: ct);

        result.Should().BeFalse();
        await settingsStore.DidNotReceive().SetStringAsync(SettingKeys.GeminiModelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_BYOK_5_GeminiChatModelClient_uses_model_from_settings_store_when_request_model_is_null()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var options = Options.Create(new GeminiOptions
        {
            DefaultModelId = "gemini-3.8-flash",
            RequestTimeoutSeconds = 60,
            MaxOutputTokens = 2048
        });

        var settingsStore = Substitute.For<ISettingsStore>();
        settingsStore.GetStringAsync(SettingKeys.GeminiModelId, Arg.Any<CancellationToken>())
            .Returns("gemini-3.6-flash");

        var client = new GeminiChatModelClient(httpClient, options, settingsStore: settingsStore);

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Tạo lịch") }
        };

        await client.SendAsync(request, ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("models/gemini-3.6-flash:generateContent");
    }
}
