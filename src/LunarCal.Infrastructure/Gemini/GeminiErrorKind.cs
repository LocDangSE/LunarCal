namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Categorized error reasons from the Gemini REST API (ADR-0016 §4, FR-BYOK-2, FR-CHAT-7).
/// </summary>
public enum GeminiErrorKind
{
    /// <summary>Missing Gemini API key in secret storage.</summary>
    MissingApiKey,

    /// <summary>API key is invalid or lacks permission (HTTP 400 API_KEY_INVALID, 401, 403).</summary>
    InvalidApiKey,

    /// <summary>API key has been reported as leaked by Google (HTTP 403 with "reported as leaked").</summary>
    LeakedApiKey,

    /// <summary>Google project requires billing verification or is ineligible for free tier (HTTP 403 FREE_TIER_VERIFICATION_REQUIRED).</summary>
    FreeTierVerificationRequired,

    /// <summary>Preconditions failed, e.g. billing or region restrictions (HTTP 400 FAILED_PRECONDITION).</summary>
    FailedPrecondition,

    /// <summary>Target model does not exist or was deprecated/removed (HTTP 404).</summary>
    ModelNotFound,

    /// <summary>Per-minute rate limit exhausted (HTTP 429 rate limit).</summary>
    RateLimitExceeded,

    /// <summary>Daily project quota exhausted (HTTP 429 quota_exceeded / daily limit).</summary>
    DailyQuotaExceeded,

    /// <summary>Transient service failure (HTTP 500, 503, 408 or connection timeout).</summary>
    TransientError,

    /// <summary>Request or response was blocked by Google safety filters (SAFETY, PROHIBITED_CONTENT).</summary>
    SafetyBlocked,

    /// <summary>Model returned malformed output or unexpected JSON schema.</summary>
    MalformedResponse
}
