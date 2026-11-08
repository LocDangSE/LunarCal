namespace LunarCal.Core.Abstractions;

/// <summary>
/// Status of an AI API key validation attempt (FR-BYOK-2, SEC-KEY-6, ADR-0016 §4).
/// </summary>
public enum KeyValidationStatus
{
    /// <summary>Key was successfully validated with the provider.</summary>
    Valid,

    /// <summary>Provided key is null, empty or only whitespace.</summary>
    EmptyKey,

    /// <summary>Key is invalid, expired or lacks necessary permissions (HTTP 400 API_KEY_INVALID, 401, 403).</summary>
    InvalidKey,

    /// <summary>Key has been reported as leaked by Google and disabled (HTTP 403 reported as leaked).</summary>
    LeakedKey,

    /// <summary>Project requires billing verification or is ineligible for free tier (HTTP 403 FREE_TIER_VERIFICATION_REQUIRED).</summary>
    FreeTierVerificationRequired,

    /// <summary>Request failed preconditions, such as region or billing requirements (HTTP 400 FAILED_PRECONDITION).</summary>
    PreconditionFailed,

    /// <summary>Quota or rate limit was exhausted (HTTP 429).</summary>
    QuotaExceeded,

    /// <summary>Network or DNS failure prevented validation (offline).</summary>
    NetworkError,

    /// <summary>Unexpected or transient error occurred.</summary>
    UnexpectedError
}

/// <summary>
/// Detailed result of an AI API key validation attempt.
/// </summary>
public sealed record KeyValidationResult(
    KeyValidationStatus Status,
    string? Message = null,
    IReadOnlyList<string>? AvailableModels = null)
{
    /// <summary>Whether the validation succeeded.</summary>
    public bool IsSuccess => Status == KeyValidationStatus.Valid;

    /// <summary>Creates a successful validation result.</summary>
    public static KeyValidationResult Success(IReadOnlyList<string>? models = null) =>
        new(KeyValidationStatus.Valid, AvailableModels: models);

    /// <summary>Creates a failed validation result.</summary>
    public static KeyValidationResult Failed(KeyValidationStatus status, string? message = null) =>
        new(status, message);
}
