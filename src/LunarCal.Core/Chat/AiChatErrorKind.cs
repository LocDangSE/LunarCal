namespace LunarCal.Core.Chat;

/// <summary>
/// Classified error categories for AI chat requests (FR-CHAT-7, ADR-0016 §4).
/// Provider-neutral enumeration defining system actions and user communication.
/// </summary>
public enum AiChatErrorKind
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>API key is missing in secure storage.</summary>
    MissingApiKey,

    /// <summary>API key is invalid, revoked, or lacks required permissions (HTTP 400 API_KEY_INVALID, 401, 403).</summary>
    InvalidApiKey,

    /// <summary>API key was reported as leaked by Google and disabled (HTTP 403 reported as leaked).</summary>
    LeakedApiKey,

    /// <summary>Google Cloud project requires billing verification or is ineligible for free tier (HTTP 403 FREE_TIER_VERIFICATION_REQUIRED).</summary>
    FreeTierVerificationRequired,

    /// <summary>Precondition failed, e.g. region or service limitations (HTTP 400 FAILED_PRECONDITION).</summary>
    FailedPrecondition,

    /// <summary>Target model does not exist or has been removed/deprecated (HTTP 404).</summary>
    ModelNotFound,

    /// <summary>Per-minute rate limit exhausted (HTTP 429 rate limit).</summary>
    RateLimitExceeded,

    /// <summary>Daily project quota exhausted (HTTP 429 quota_exceeded / daily limit).</summary>
    DailyQuotaExceeded,

    /// <summary>Content was blocked by safety filters (SAFETY, PROHIBITED_CONTENT).</summary>
    SafetyBlocked,

    /// <summary>No network connectivity or host unreachable (offline, DNS failure, connection refused).</summary>
    NetworkUnavailable,

    /// <summary>Transient service failure (HTTP 500, 503, 408, request timeout).</summary>
    TransientError,

    /// <summary>Malformed model output or unexpected schema.</summary>
    MalformedResponse,

    /// <summary>Unclassified or unexpected error.</summary>
    Unknown
}
