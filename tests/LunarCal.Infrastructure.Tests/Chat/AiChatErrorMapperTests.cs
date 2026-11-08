using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Chat;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Chat;

/// <summary>
/// FR-CHAT-7, ADR-0016 §4: Tests for classifying and mapping AI chat errors.
/// Verifies:
/// 1. Key errors (invalid, missing, leaked, billing, model not found) -> Open Settings AI, no retry.
/// 2. Rate limit 429 -> cooldown waiting, retry with backoff.
/// 3. Daily quota 429 -> report quota reset time, no retry.
/// 4. Safety blocked -> report safety filter rejection, no retry.
/// 5. Network loss -> preserved locally in queue, automatically re-processed when network returns.
/// 6. Transient errors (500, 503, timeout) -> retry with backoff.
/// 7. Redaction of sensitive credentials from technical details (SEC-KEY-3, SEC-LOG-1).
/// </summary>
public sealed class AiChatErrorMapperTests
{
    private readonly AiChatErrorMapper _mapper = new();

    [Fact]
    public void FR_CHAT_7_Maps_null_exception_to_unknown_without_throwing()
    {
        var result = _mapper.Map(null);

        result.Kind.Should().Be(AiChatErrorKind.None);
        result.Action.Should().Be(AiChatErrorAction.None);
        result.CanRetry.Should().BeFalse();
        result.RequiresSettingsNavigation.Should().BeFalse();
    }

    [Theory]
    [InlineData(GeminiErrorKind.InvalidApiKey, AiChatErrorKind.InvalidApiKey, "ai_error.invalid_key")]
    [InlineData(GeminiErrorKind.MissingApiKey, AiChatErrorKind.MissingApiKey, "ai_error.missing_key")]
    [InlineData(GeminiErrorKind.LeakedApiKey, AiChatErrorKind.LeakedApiKey, "ai_error.leaked_key")]
    [InlineData(GeminiErrorKind.ModelNotFound, AiChatErrorKind.ModelNotFound, "ai_error.model_not_found")]
    [InlineData(GeminiErrorKind.FreeTierVerificationRequired, AiChatErrorKind.FreeTierVerificationRequired, "ai_error.free_tier")]
    public void FR_CHAT_7_Key_and_model_errors_require_settings_navigation_and_cannot_retry(
        GeminiErrorKind geminiKind,
        AiChatErrorKind expectedKind,
        string expectedMessageKey)
    {
        var ex = new GeminiApiException(geminiKind, "API failure for key or model");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(expectedKind);
        result.Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        result.MessageKey.Should().Be(expectedMessageKey);
        result.RequiresSettingsNavigation.Should().BeTrue();
        result.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_Rate_limit_429_requires_waiting_and_can_retry()
    {
        var ex = new GeminiApiException(
            GeminiErrorKind.RateLimitExceeded,
            "Resource exhausted: 15 requests per minute limit reached",
            statusCode: (HttpStatusCode)429,
            geminiStatus: "RESOURCE_EXHAUSTED");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.RateLimitExceeded);
        result.Action.Should().Be(AiChatErrorAction.WaitForRetry);
        result.MessageKey.Should().Be("ai_error.rate_limit");
        result.CanRetry.Should().BeTrue();
        result.RetryAfter.Should().NotBeNull();
        result.RetryAfter!.Value.TotalSeconds.Should().BeGreaterThan(0);
        result.RequiresSettingsNavigation.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_Daily_quota_429_notifies_daily_quota_and_cannot_retry()
    {
        var ex = new GeminiApiException(
            GeminiErrorKind.DailyQuotaExceeded,
            "Resource exhausted: daily quota for this project has been exhausted",
            statusCode: (HttpStatusCode)429,
            geminiStatus: "RESOURCE_EXHAUSTED");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.DailyQuotaExceeded);
        result.Action.Should().Be(AiChatErrorAction.NotifyDailyQuotaExceeded);
        result.MessageKey.Should().Be("ai_error.daily_quota");
        result.CanRetry.Should().BeFalse();
        result.RequiresSettingsNavigation.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_Safety_blocked_notifies_safety_filter_and_cannot_retry()
    {
        var ex = new GeminiApiException(
            GeminiErrorKind.SafetyBlocked,
            "Candidate was blocked due to SAFETY",
            geminiStatus: "BLOCKED_BY_SAFETY");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.SafetyBlocked);
        result.Action.Should().Be(AiChatErrorAction.NotifySafetyBlock);
        result.MessageKey.Should().Be("ai_error.safety_blocked");
        result.CanRetry.Should().BeFalse();
        result.RequiresSettingsNavigation.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_Transient_500_503_timeout_can_retry()
    {
        var ex = new GeminiApiException(
            GeminiErrorKind.TransientError,
            "Internal server error",
            statusCode: HttpStatusCode.ServiceUnavailable);

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.TransientError);
        result.Action.Should().Be(AiChatErrorAction.WaitForRetry);
        result.MessageKey.Should().Be("ai_error.transient");
        result.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_Timeout_exception_is_mapped_as_transient_retryable()
    {
        var ex = new TimeoutException("The HTTP request timed out after 60 seconds");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.TransientError);
        result.Action.Should().Be(AiChatErrorAction.WaitForRetry);
        result.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_Socket_exception_is_mapped_as_network_unavailable_queued_locally()
    {
        var ex = new SocketException((int)SocketError.HostNotFound);

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.NetworkUnavailable);
        result.Action.Should().Be(AiChatErrorAction.QueueOffline);
        result.MessageKey.Should().Be("ai_error.network_offline");
        result.CanRetry.Should().BeFalse();
        result.IsQueuedLocally.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_HttpRequestException_without_status_is_mapped_as_network_unavailable()
    {
        var ex = new HttpRequestException("No such host is known. (generativelanguage.googleapis.com:443)");

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.NetworkUnavailable);
        result.Action.Should().Be(AiChatErrorAction.QueueOffline);
        result.MessageKey.Should().Be("ai_error.network_offline");
        result.CanRetry.Should().BeFalse();
        result.IsQueuedLocally.Should().BeTrue();
    }

    [Fact]
    public void FR_CHAT_7_HttpRequestException_401_maps_to_invalid_key_and_settings()
    {
        var ex = new HttpRequestException(
            "Response status code does not indicate success: 401 (Unauthorized).",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.InvalidApiKey);
        result.Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        result.RequiresSettingsNavigation.Should().BeTrue();
        result.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_HttpRequestException_403_leaked_key_maps_to_leaked_key()
    {
        var ex = new HttpRequestException(
            "Your API key was reported as leaked and has been disabled.",
            inner: null,
            statusCode: HttpStatusCode.Forbidden);

        var result = _mapper.Map(ex);

        result.Kind.Should().Be(AiChatErrorKind.LeakedApiKey);
        result.Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        result.RequiresSettingsNavigation.Should().BeTrue();
        result.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_MapMessage_classifies_raw_strings_accurately()
    {
        _mapper.MapMessage("Generation was blocked by safety filters.").Kind.Should().Be(AiChatErrorKind.SafetyBlocked);
        _mapper.MapMessage("API_KEY_INVALID: The key does not exist.").Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        _mapper.MapMessage("Target model not found: models/gemini-old").Action.Should().Be(AiChatErrorAction.OpenAiSettings);
        _mapper.MapMessage("HTTP 429: rate limit exceeded").Action.Should().Be(AiChatErrorAction.WaitForRetry);
        _mapper.MapMessage("Quota exceeded: daily limit reached").Action.Should().Be(AiChatErrorAction.NotifyDailyQuotaExceeded);
        _mapper.MapMessage("Network connection lost; offline mode").Action.Should().Be(AiChatErrorAction.QueueOffline);
    }

    [Fact]
    public void SEC_KEY_3_Technical_details_redacts_api_key_from_error_result()
    {
        const string rawKey = "AIzaSyDummySecretKeyForTestOnly123456";
        var ex = new InvalidOperationException($"Failed connecting with key {rawKey} to Gemini");

        var result = _mapper.Map(ex);

        result.TechnicalDetails.Should().NotContain(rawKey);
        result.TechnicalDetails.Should().Contain("[REDACTED");
    }
}
