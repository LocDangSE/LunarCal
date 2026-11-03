using System.Net;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Network;

public class ResiliencePipelineIntegrationTests
{
    private static HttpClient CreateTestClient(
        HttpMessageHandler innerHandler,
        NetworkOptions? networkOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var services = new ServiceCollection();
        var options = networkOptions ?? new NetworkOptions
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(5)
        };

        services.AddSingleton(options);
        services.AddTransient(sp => new NetworkSecurityDelegatingHandler(options));

        services.AddHttpClient("TestClient")
            .ConfigurePrimaryHttpMessageHandler(() => innerHandler)
            .AddHttpMessageHandler<NetworkSecurityDelegatingHandler>()
            .AddLunarCalResilienceHandler(options, resOptions =>
            {
                resOptions.Retry.Delay = TimeSpan.FromMilliseconds(5);
                configureResilience?.Invoke(resOptions);
            });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient("TestClient");
    }

    [Fact]
    public async Task SEC_NET_1_Request_to_host_outside_allow_list_is_blocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new CountingFakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://malicious-server.com/steal");

        var ex = await Assert.ThrowsAsync<NetworkSecurityException>(() => client.SendAsync(request, ct));

        Assert.Equal(NetworkSecurityViolationReason.DisallowedHost, ex.Reason);
        Assert.Equal("malicious-server.com", ex.BlockedHost);
        Assert.Equal(0, fakeHandler.CallCount);
    }

    [Fact]
    public async Task SEC_NET_1_Insecure_http_scheme_is_blocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new CountingFakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://accounts.google.com/oauth");

        var ex = await Assert.ThrowsAsync<NetworkSecurityException>(() => client.SendAsync(request, ct));

        Assert.Equal(NetworkSecurityViolationReason.InsecureScheme, ex.Reason);
        Assert.Equal(0, fakeHandler.CallCount);
    }

    [Fact]
    public async Task SEC_NET_2_POST_without_requestId_or_ETag_is_not_retried_on_transient_failure()
    {
        // When a POST request fails with 503 (transient), but lacks requestId or ETag,
        // SEC-NET-2 mandates it MUST NOT be retried.
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new CountingFakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, fakeHandler.CallCount); // Exactly 1 call, NO retries!
    }

    [Fact]
    public async Task SEC_NET_2_POST_with_requestId_is_retried_on_transient_failure()
    {
        // When a POST request carries a requestId, it is idempotent and safe to retry.
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        var fakeHandler = new CountingFakeHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events")
            .WithRequestId("req-retry-uuid-1");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fakeHandler.CallCount); // Retried once and succeeded on attempt 2!
    }

    [Fact]
    public async Task SEC_NET_2_POST_with_ETag_is_retried_on_transient_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        var fakeHandler = new CountingFakeHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events")
            .WithETag("\"etag-version-123\"");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fakeHandler.CallCount);
    }

    [Fact]
    public async Task SEC_NET_2_GET_is_retried_on_transient_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        var fakeHandler = new CountingFakeHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = CreateTestClient(fakeHandler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://calendar.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fakeHandler.CallCount);
    }

    [Fact]
    public void SEC_NET_1_Primary_handler_does_not_bypass_certificate_validation()
    {
        using var handler = ResiliencePipelineExtensions.CreateSecureSocketsHttpHandler();

        // SEC-NET-1: Certificate validation must NOT be bypassed
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    private sealed class CountingFakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseProvider;
        public int CallCount { get; private set; }

        public CountingFakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responseProvider)
        {
            _responseProvider = responseProvider;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responseProvider(request));
        }
    }
}
