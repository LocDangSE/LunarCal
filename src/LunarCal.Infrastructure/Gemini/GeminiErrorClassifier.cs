using System.Net;
using System.Text.Json;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Classifies Gemini REST HTTP response errors according to ADR-0016 §4 (FR-BYOK-2, FR-CHAT-7).
/// </summary>
public static class GeminiErrorClassifier
{
    public static (GeminiErrorKind Kind, string Description, string? Status, string? Message) Classify(
        HttpStatusCode statusCode,
        string? responseBody)
    {
        string? status = null;
        string? message = null;
        string? reason = null;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.TryGetProperty("status", out var statusProp))
                    {
                        status = statusProp.GetString();
                    }

                    if (errorElement.TryGetProperty("message", out var msgProp))
                    {
                        message = msgProp.GetString();
                    }

                    if (errorElement.TryGetProperty("details", out var detailsProp) &&
                        detailsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in detailsProp.EnumerateArray())
                        {
                            if (detail.TryGetProperty("reason", out var reasonProp))
                            {
                                reason = reasonProp.GetString();
                                break;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Unparseable body - fallback to status code analysis
            }
        }

        var combined = $"{status} {reason} {message}".ToLowerInvariant();

        // 1. 403 Leaked API key
        if (statusCode == HttpStatusCode.Forbidden && combined.Contains("reported as leaked"))
        {
            return (GeminiErrorKind.LeakedApiKey,
                "API key has been reported as leaked by Google and disabled.", status, message);
        }

        // 2. 403 Free tier verification required
        if (statusCode == HttpStatusCode.Forbidden &&
            (combined.Contains("free_tier_verification_required") || combined.Contains("billing")))
        {
            return (GeminiErrorKind.FreeTierVerificationRequired,
                "Project requires billing verification or is ineligible for free tier.", status, message);
        }

        // 3. 400 API_KEY_INVALID, 401, 403 generic
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            combined.Contains("api_key_invalid") ||
            combined.Contains("api key not valid") ||
            combined.Contains("invalid api key"))
        {
            return (GeminiErrorKind.InvalidApiKey,
                "API key is invalid or lacks necessary permissions for Gemini API.", status, message);
        }

        // 4. 400 FAILED_PRECONDITION
        if (string.Equals(status, "FAILED_PRECONDITION", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("failed_precondition"))
        {
            return (GeminiErrorKind.FailedPrecondition,
                "Google Gemini request failed precondition (e.g. region or service requirements).", status, message);
        }

        // 5. 404 Model not found
        if (statusCode == HttpStatusCode.NotFound || combined.Contains("not_found") || combined.Contains("model not found"))
        {
            return (GeminiErrorKind.ModelNotFound,
                "Target Gemini model not found or has been deprecated.", status, message);
        }

        // 6. 429 Rate limit vs Daily Quota
        if ((int)statusCode == 429 || string.Equals(status, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
        {
            if (combined.Contains("daily") || combined.Contains("per day") || combined.Contains("quota_exceeded"))
            {
                return (GeminiErrorKind.DailyQuotaExceeded,
                    "Daily quota for this project has been exhausted. Quota resets at midnight Pacific Time.", status, message);
            }

            return (GeminiErrorKind.RateLimitExceeded,
                "Rate limit exceeded (requests per minute). Please retry with backoff.", status, message);
        }

        // 7. 500, 503, 408 Transient error
        if (statusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
        {
            return (GeminiErrorKind.TransientError,
                "Gemini service reported a transient error.", status, message);
        }

        // 8. Other client error
        if ((int)statusCode >= 400 && (int)statusCode < 500)
        {
            return (GeminiErrorKind.MalformedResponse,
                $"Gemini API returned client error (HTTP {(int)statusCode}): {message ?? "Invalid request"}", status, message);
        }

        return (GeminiErrorKind.TransientError,
            $"Gemini API returned error (HTTP {(int)statusCode}): {message ?? "Unknown error"}", status, message);
    }
}
