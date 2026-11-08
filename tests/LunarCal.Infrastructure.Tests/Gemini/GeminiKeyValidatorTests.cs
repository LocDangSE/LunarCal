using System.Net;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Gemini;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiKeyValidator"/> (FR-BYOK-2, SEC-KEY-6, ADR-0016 §4).
/// </summary>
public sealed class GeminiKeyValidatorTests
{
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public Exception? ExceptionToThrow { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Response);
        }
    }

    [Fact]
    public async Task FR_BYOK_2_Empty_or_whitespace_key_returns_EmptyKey_without_network_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler();
        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var resultEmpty = await validator.ValidateKeyAsync(string.Empty, ct);
        var resultWhitespace = await validator.ValidateKeyAsync("   ", ct);

        resultEmpty.Status.Should().Be(KeyValidationStatus.EmptyKey);
        resultEmpty.IsSuccess.Should().BeFalse();
        resultWhitespace.Status.Should().Be(KeyValidationStatus.EmptyKey);
        handler.CapturedRequest.Should().BeNull();
    }

    [Fact]
    public async Task FR_BYOK_2_Valid_key_returns_Success_and_extracts_model_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "models": [
                    {
                      "name": "models/gemini-3.8-flash",
                      "displayName": "Gemini 3.8 Flash"
                    },
                    {
                      "name": "models/gemini-3.5-flash-lite",
                      "displayName": "Gemini 3.5 Flash Lite"
                    }
                  ]
                }
                """, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("valid_key_test_123", ct);

        result.Status.Should().Be(KeyValidationStatus.Valid);
        result.IsSuccess.Should().BeTrue();
        result.AvailableModels.Should().NotBeNull();
        result.AvailableModels!.Should().Contain("gemini-3.8-flash");
        result.AvailableModels!.Should().Contain("gemini-3.5-flash-lite");

        // Verify request headers
        handler.CapturedRequest.Should().NotBeNull();
        handler.CapturedRequest!.Headers.Contains("x-goog-api-key").Should().BeTrue();
        handler.CapturedRequest!.Headers.GetValues("x-goog-api-key").First().Should().Be("valid_key_test_123");
        handler.CapturedRequest!.RequestUri!.Query.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key not valid", KeyValidationStatus.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, "The caller does not have permission", KeyValidationStatus.InvalidKey)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"message\":\"API_KEY_INVALID\"}}", KeyValidationStatus.InvalidKey)]
    public async Task FR_BYOK_2_Invalid_key_returns_InvalidKey_status(HttpStatusCode statusCode, string body, KeyValidationStatus expectedStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("bad_key_value", ct);

        result.Status.Should().Be(expectedStatus);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SEC_KEY_7_Leaked_key_reported_by_google_returns_LeakedKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""
                {
                  "error": {
                    "code": 403,
                    "message": "Your API key has been reported as leaked and is compromised.",
                    "status": "PERMISSION_DENIED"
                  }
                }
                """, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("leaked_key_val", ct);

        result.Status.Should().Be(KeyValidationStatus.LeakedKey);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FR_BYOK_2_Free_tier_verification_required_returns_FreeTierVerificationRequired()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""
                {
                  "error": {
                    "code": 403,
                    "message": "FREE_TIER_VERIFICATION_REQUIRED: Project requires billing verification.",
                    "status": "PERMISSION_DENIED"
                  }
                }
                """, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("test_key", ct);

        result.Status.Should().Be(KeyValidationStatus.FreeTierVerificationRequired);
    }

    [Fact]
    public async Task FR_BYOK_2_Failed_precondition_returns_PreconditionFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""
                {
                  "error": {
                    "code": 400,
                    "message": "User location is not supported for the API use.",
                    "status": "FAILED_PRECONDITION"
                  }
                }
                """, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("test_key", ct);

        result.Status.Should().Be(KeyValidationStatus.PreconditionFailed);
    }

    [Fact]
    public async Task FR_BYOK_2_Rate_limit_or_quota_exceeded_returns_QuotaExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""
                {
                  "error": {
                    "code": 429,
                    "message": "Quota exceeded for quota metric 'Generate Content API Requests Per Day'.",
                    "status": "RESOURCE_EXHAUSTED"
                  }
                }
                """, System.Text.Encoding.UTF8, "application/json")
            }
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("test_key", ct);

        result.Status.Should().Be(KeyValidationStatus.QuotaExceeded);
    }

    [Fact]
    public async Task FR_BYOK_2_Network_failure_returns_NetworkError()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpHandler
        {
            ExceptionToThrow = new HttpRequestException("DNS resolution failed or offline")
        };

        var client = new HttpClient(handler);
        var validator = new GeminiKeyValidator(client);

        var result = await validator.ValidateKeyAsync("test_key", ct);

        result.Status.Should().Be(KeyValidationStatus.NetworkError);
    }
}
