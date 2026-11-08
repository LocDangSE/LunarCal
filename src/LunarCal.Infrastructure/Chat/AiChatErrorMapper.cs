using System.Net;
using System.Net.Sockets;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;

namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-CHAT-7, ADR-0016 §4: Concrete mapper classifying AI chat exceptions and messages
/// into structured actions (Settings navigation, cooldown waiting, offline local queue, safety notifications).
/// Protects sensitive credentials via <see cref="LogRedactor"/> (SEC-KEY-3, SEC-LOG-1).
/// </summary>
public sealed class AiChatErrorMapper : IAiChatErrorMapper
{
    private static readonly TimeSpan _defaultRateLimitCooldown = TimeSpan.FromSeconds(30);

    public AiChatErrorResult Map(Exception? exception)
    {
        if (exception is null)
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.None,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.unknown");
        }

        var technicalDetails = LogRedactor.Redact(exception.Message);

        // 1. Direct GeminiApiException
        if (exception is GeminiApiException geminiEx)
        {
            return MapGeminiException(geminiEx);
        }

        // 2. Network and socket exceptions
        if (exception is HttpRequestException httpEx)
        {
            return MapHttpRequestException(httpEx);
        }

        if (exception is SocketException sockEx)
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.NetworkUnavailable,
                Action: AiChatErrorAction.QueueOffline,
                MessageKey: "ai_error.network_offline",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                IsQueuedLocally: true);
        }

        // 3. Timeout exceptions
        if (exception is TimeoutException || exception is TaskCanceledException)
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.TransientError,
                Action: AiChatErrorAction.WaitForRetry,
                MessageKey: "ai_error.transient",
                TechnicalDetails: technicalDetails,
                CanRetry: true);
        }

        // 4. Fallback message inspection
        return MapMessageInternal(exception.Message, technicalDetails);
    }

    public AiChatErrorResult MapMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.Unknown,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.unknown");
        }

        var technicalDetails = LogRedactor.Redact(errorMessage);
        return MapMessageInternal(errorMessage, technicalDetails);
    }

    private static AiChatErrorResult MapGeminiException(GeminiApiException ex)
    {
        var technical = LogRedactor.Redact(ex.Message);

        return ex.ErrorKind switch
        {
            GeminiErrorKind.MissingApiKey => new AiChatErrorResult(
                Kind: AiChatErrorKind.MissingApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.missing_key",
                TechnicalDetails: technical,
                CanRetry: false,
                RequiresSettingsNavigation: true),

            GeminiErrorKind.InvalidApiKey => new AiChatErrorResult(
                Kind: AiChatErrorKind.InvalidApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.invalid_key",
                TechnicalDetails: technical,
                CanRetry: false,
                RequiresSettingsNavigation: true),

            GeminiErrorKind.LeakedApiKey => new AiChatErrorResult(
                Kind: AiChatErrorKind.LeakedApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.leaked_key",
                TechnicalDetails: technical,
                CanRetry: false,
                RequiresSettingsNavigation: true),

            GeminiErrorKind.FreeTierVerificationRequired => new AiChatErrorResult(
                Kind: AiChatErrorKind.FreeTierVerificationRequired,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.free_tier",
                TechnicalDetails: technical,
                CanRetry: false,
                RequiresSettingsNavigation: true),

            GeminiErrorKind.FailedPrecondition => new AiChatErrorResult(
                Kind: AiChatErrorKind.FailedPrecondition,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.failed_precondition",
                TechnicalDetails: technical,
                CanRetry: false),

            GeminiErrorKind.ModelNotFound => new AiChatErrorResult(
                Kind: AiChatErrorKind.ModelNotFound,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.model_not_found",
                TechnicalDetails: technical,
                CanRetry: false,
                RequiresSettingsNavigation: true),

            GeminiErrorKind.RateLimitExceeded => new AiChatErrorResult(
                Kind: AiChatErrorKind.RateLimitExceeded,
                Action: AiChatErrorAction.WaitForRetry,
                MessageKey: "ai_error.rate_limit",
                TechnicalDetails: technical,
                RetryAfter: _defaultRateLimitCooldown,
                CanRetry: true),

            GeminiErrorKind.DailyQuotaExceeded => new AiChatErrorResult(
                Kind: AiChatErrorKind.DailyQuotaExceeded,
                Action: AiChatErrorAction.NotifyDailyQuotaExceeded,
                MessageKey: "ai_error.daily_quota",
                TechnicalDetails: technical,
                CanRetry: false),

            GeminiErrorKind.SafetyBlocked => new AiChatErrorResult(
                Kind: AiChatErrorKind.SafetyBlocked,
                Action: AiChatErrorAction.NotifySafetyBlock,
                MessageKey: "ai_error.safety_blocked",
                TechnicalDetails: technical,
                CanRetry: false),

            GeminiErrorKind.TransientError => new AiChatErrorResult(
                Kind: AiChatErrorKind.TransientError,
                Action: AiChatErrorAction.WaitForRetry,
                MessageKey: "ai_error.transient",
                TechnicalDetails: technical,
                CanRetry: true),

            GeminiErrorKind.MalformedResponse => new AiChatErrorResult(
                Kind: AiChatErrorKind.MalformedResponse,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.malformed",
                TechnicalDetails: technical,
                CanRetry: false),

            _ => new AiChatErrorResult(
                Kind: AiChatErrorKind.Unknown,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.unknown",
                TechnicalDetails: technical,
                CanRetry: false),
        };
    }

    private static AiChatErrorResult MapHttpRequestException(HttpRequestException ex)
    {
        var technical = LogRedactor.Redact(ex.Message);

        if (ex.StatusCode.HasValue)
        {
            var code = ex.StatusCode.Value;

            if (code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var lower = ex.Message.ToLowerInvariant();
                if (lower.Contains("reported as leaked"))
                {
                    return new AiChatErrorResult(
                        Kind: AiChatErrorKind.LeakedApiKey,
                        Action: AiChatErrorAction.OpenAiSettings,
                        MessageKey: "ai_error.leaked_key",
                        TechnicalDetails: technical,
                        CanRetry: false,
                        RequiresSettingsNavigation: true);
                }

                return new AiChatErrorResult(
                    Kind: AiChatErrorKind.InvalidApiKey,
                    Action: AiChatErrorAction.OpenAiSettings,
                    MessageKey: "ai_error.invalid_key",
                    TechnicalDetails: technical,
                    CanRetry: false,
                    RequiresSettingsNavigation: true);
            }

            if (code == HttpStatusCode.NotFound)
            {
                return new AiChatErrorResult(
                    Kind: AiChatErrorKind.ModelNotFound,
                    Action: AiChatErrorAction.OpenAiSettings,
                    MessageKey: "ai_error.model_not_found",
                    TechnicalDetails: technical,
                    CanRetry: false,
                    RequiresSettingsNavigation: true);
            }

            if ((int)code == 429)
            {
                var lower = ex.Message.ToLowerInvariant();
                if (lower.Contains("daily") || lower.Contains("quota_exceeded") || (lower.Contains("quota") && lower.Contains("limit")))
                {
                    return new AiChatErrorResult(
                        Kind: AiChatErrorKind.DailyQuotaExceeded,
                        Action: AiChatErrorAction.NotifyDailyQuotaExceeded,
                        MessageKey: "ai_error.daily_quota",
                        TechnicalDetails: technical,
                        CanRetry: false);
                }

                return new AiChatErrorResult(
                    Kind: AiChatErrorKind.RateLimitExceeded,
                    Action: AiChatErrorAction.WaitForRetry,
                    MessageKey: "ai_error.rate_limit",
                    TechnicalDetails: technical,
                    RetryAfter: _defaultRateLimitCooldown,
                    CanRetry: true);
            }

            if (code is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            {
                return new AiChatErrorResult(
                    Kind: AiChatErrorKind.TransientError,
                    Action: AiChatErrorAction.WaitForRetry,
                    MessageKey: "ai_error.transient",
                    TechnicalDetails: technical,
                    CanRetry: true);
            }
        }

        // If no status code or socket-level/network transport failure
        return new AiChatErrorResult(
            Kind: AiChatErrorKind.NetworkUnavailable,
            Action: AiChatErrorAction.QueueOffline,
            MessageKey: "ai_error.network_offline",
            TechnicalDetails: technical,
            CanRetry: false,
            IsQueuedLocally: true);
    }

    private static AiChatErrorResult MapMessageInternal(string message, string technicalDetails)
    {
        var lower = message.ToLowerInvariant();

        if (lower.Contains("reported as leaked"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.LeakedApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.leaked_key",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                RequiresSettingsNavigation: true);
        }

        if (lower.Contains("api_key_invalid") ||
            lower.Contains("api key not valid") ||
            lower.Contains("invalid api key") ||
            lower.Contains("api key is invalid") ||
            lower.Contains("unauthorized") ||
            lower.Contains("401") ||
            lower.Contains("403"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.InvalidApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.invalid_key",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                RequiresSettingsNavigation: true);
        }

        if (lower.Contains("missing api key") ||
            lower.Contains("no gemini api key") ||
            lower.Contains("chưa cấu hình api key"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.MissingApiKey,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.missing_key",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                RequiresSettingsNavigation: true);
        }

        if (lower.Contains("model_not_found") ||
            lower.Contains("model not found") ||
            lower.Contains("target model not found") ||
            lower.Contains("404"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.ModelNotFound,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.model_not_found",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                RequiresSettingsNavigation: true);
        }

        if (lower.Contains("free_tier_verification_required") || lower.Contains("billing"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.FreeTierVerificationRequired,
                Action: AiChatErrorAction.OpenAiSettings,
                MessageKey: "ai_error.free_tier",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                RequiresSettingsNavigation: true);
        }

        if (lower.Contains("failed_precondition"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.FailedPrecondition,
                Action: AiChatErrorAction.None,
                MessageKey: "ai_error.failed_precondition",
                TechnicalDetails: technicalDetails,
                CanRetry: false);
        }

        if (lower.Contains("quota_exceeded") ||
            (lower.Contains("quota") && lower.Contains("daily")) ||
            (lower.Contains("limit") && lower.Contains("daily")) ||
            lower.Contains("per day") ||
            lower.Contains("hạn mức ngày"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.DailyQuotaExceeded,
                Action: AiChatErrorAction.NotifyDailyQuotaExceeded,
                MessageKey: "ai_error.daily_quota",
                TechnicalDetails: technicalDetails,
                CanRetry: false);
        }

        if (lower.Contains("rate limit") ||
            lower.Contains("resource_exhausted") ||
            lower.Contains("too many requests") ||
            lower.Contains("429"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.RateLimitExceeded,
                Action: AiChatErrorAction.WaitForRetry,
                MessageKey: "ai_error.rate_limit",
                TechnicalDetails: technicalDetails,
                RetryAfter: _defaultRateLimitCooldown,
                CanRetry: true);
        }

        if (lower.Contains("safety") ||
            lower.Contains("prohibited_content") ||
            lower.Contains("blocked by safety filters") ||
            lower.Contains("bị chặn an toàn"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.SafetyBlocked,
                Action: AiChatErrorAction.NotifySafetyBlock,
                MessageKey: "ai_error.safety_blocked",
                TechnicalDetails: technicalDetails,
                CanRetry: false);
        }

        if (lower.Contains("offline") ||
            lower.Contains("network") ||
            lower.Contains("no such host") ||
            lower.Contains("connection refused") ||
            lower.Contains("mất kết nối") ||
            lower.Contains("ngoại tuyến"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.NetworkUnavailable,
                Action: AiChatErrorAction.QueueOffline,
                MessageKey: "ai_error.network_offline",
                TechnicalDetails: technicalDetails,
                CanRetry: false,
                IsQueuedLocally: true);
        }

        if (lower.Contains("500") ||
            lower.Contains("503") ||
            lower.Contains("transient") ||
            lower.Contains("timeout") ||
            lower.Contains("timed out"))
        {
            return new AiChatErrorResult(
                Kind: AiChatErrorKind.TransientError,
                Action: AiChatErrorAction.WaitForRetry,
                MessageKey: "ai_error.transient",
                TechnicalDetails: technicalDetails,
                CanRetry: true);
        }

        return new AiChatErrorResult(
            Kind: AiChatErrorKind.Unknown,
            Action: AiChatErrorAction.None,
            MessageKey: "ai_error.unknown",
            TechnicalDetails: technicalDetails,
            CanRetry: false);
    }
}
