using System.Net;
using System.Text;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Network;

public class GoogleRateLimitResilienceTests
{
    private static HttpClient CreateTestResilientClient(
        HttpMessageHandler innerHandler,
        NetworkOptions? networkOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var services = new ServiceCollection();
        var options = networkOptions ?? new NetworkOptions
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryAttempts = 3
        };

        services.AddSingleton(options);
        services.AddTransient(sp => new NetworkSecurityDelegatingHandler(options));

        services.AddHttpClient("GoogleTestClient")
            .ConfigurePrimaryHttpMessageHandler(() => innerHandler)
            .AddHttpMessageHandler<NetworkSecurityDelegatingHandler>()
            .AddLunarCalResilienceHandler(options, resOptions =>
            {
                resOptions.Retry.Delay = TimeSpan.FromMilliseconds(5);
                configureResilience?.Invoke(resOptions);
            });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient("GoogleTestClient");
    }

    [Fact]
    public async Task FR_SYNC_5_Retry_on_429_too_many_requests_with_exponential_backoff()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("Too many requests")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task FR_SYNC_5_Retry_on_403_rateLimitExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var rateLimitJson = """
        {
          "error": {
            "errors": [
              {
                "domain": "usageLimits",
                "reason": "rateLimitExceeded",
                "message": "User Rate Limit Exceeded"
              }
            ],
            "code": 403,
            "message": "User Rate Limit Exceeded"
          }
        }
        """;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(rateLimitJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task FR_SYNC_5_Retry_on_403_userRateLimitExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var userRateLimitJson = """
        {
          "error": {
            "errors": [
              {
                "domain": "usageLimits",
                "reason": "userRateLimitExceeded",
                "message": "User Rate Limit Exceeded"
              }
            ],
            "code": 403,
            "message": "User Rate Limit Exceeded"
          }
        }
        """;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(userRateLimitJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task FR_SYNC_5_Retry_on_403_quotaExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var quotaJson = """
        {
          "error": {
            "errors": [
              {
                "domain": "usageLimits",
                "reason": "quotaExceeded",
                "message": "Daily Limit Exceeded"
              }
            ],
            "code": 403,
            "message": "Daily Limit Exceeded"
          }
        }
        """;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(quotaJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task FR_SYNC_5_No_retry_on_403_non_rate_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var accessDeniedJson = """
        {
          "error": {
            "errors": [
              {
                "domain": "global",
                "reason": "accessDenied",
                "message": "Access denied"
              }
            ],
            "code": 403,
            "message": "Access denied"
          }
        }
        """;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(accessDeniedJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, callCount); // Must not retry
    }

    [Fact]
    public async Task FR_SYNC_5_No_retry_on_429_for_non_idempotent_post_without_request_id()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate limited")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/calendars/primary/events");
        request.Content = new StringContent("{\"summary\":\"Test Event\"}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal(1, callCount); // Non-idempotent write without requestId must NEVER retry (SEC-NET-2)
    }

    [Fact]
    public async Task FR_SYNC_5_Retry_on_429_for_idempotent_post_with_request_id()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("Rate limited")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"ev-1\"}", Encoding.UTF8, "application/json")
            };
        });

        using var client = CreateTestResilientClient(fakeHandler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/calendars/primary/events");
        request.Headers.Add("X-Request-Id", "req-id-12345");
        request.Content = new StringContent("{\"summary\":\"Test Event\"}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task FR_SYNC_5_CalendarGateway_GetCalendarListAsync_recovers_after_429()
    {
        var ct = TestContext.Current.CancellationToken;
        int callCount = 0;

        var validCalendarListJson = """
        {
          "items": [
            {
              "id": "user@gmail.com",
              "summary": "Personal",
              "primary": true
            }
          ]
        }
        """;

        var fakeHandler = new SimpleFakeHandler(_ =>
        {
            callCount++;
            if (callCount < 2)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("Rate limited")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(validCalendarListJson, Encoding.UTF8, "application/json")
            };
        });

        using var resilientClient = CreateTestResilientClient(fakeHandler);
        var gateway = new CalendarGateway(resilientClient);

        var result = await gateway.GetCalendarListAsync("valid-token", cancellationToken: ct);

        Assert.Single(result.Calendars);
        Assert.Equal("user@gmail.com", result.Calendars[0].Id);
        Assert.Equal(2, callCount);
    }

    private sealed class SimpleFakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public SimpleFakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_handler(request));
        }
    }
}
