namespace LunarCal.Core.Abstractions;

/// <summary>
/// Exception thrown when Google OAuth token refresh fails with 'invalid_grant' or when no stored token exists (FR-AUTH-3, FR-AUTH-6).
/// This indicates the refresh token has expired (such as after 7 days when the Google OAuth consent screen
/// is in 'Testing' mode) or was revoked, requiring the user to re-authenticate.
/// </summary>
public sealed class GoogleInvalidGrantException : Exception
{
    /// <summary>Default user guidance for Google OAuth consent screen in 'Testing' mode (FR-AUTH-6).</summary>
    public const string TestingModeDefaultGuidance =
        "The Google refresh token has expired or was revoked (invalid_grant). Re-authentication is required. " +
        "Note: Because the Google OAuth consent screen is configured in 'Testing' publishing status, refresh tokens " +
        "expire automatically after 7 days (FR-AUTH-6). To eliminate the 7-day expiration limit, publish the app " +
        "consent screen to 'In production'.";

    /// <summary>Default user guidance when consent screen is in 'In production' mode.</summary>
    public const string ProductionModeDefaultGuidance =
        "The Google refresh token is invalid or expired (invalid_grant). Re-authentication is required.";

    /// <summary>The ID of the account that failed refresh.</summary>
    public string AccountId { get; }

    /// <summary>Whether the application is configured in Google OAuth 'Testing' consent mode (FR-AUTH-6).</summary>
    public bool IsTestingConsentMode { get; }

    /// <summary>Detailed guidance message explaining the cause and remediation steps (FR-AUTH-6).</summary>
    public string GuidanceMessage { get; }

    public GoogleInvalidGrantException(
        string accountId,
        string? message = null,
        bool isTestingConsentMode = true,
        string? guidanceMessage = null,
        Exception? innerException = null)
        : base(message ?? $"Google OAuth refresh token for account '{accountId}' is invalid or expired (invalid_grant). Re-authentication required.", innerException)
    {
        AccountId = accountId;
        IsTestingConsentMode = isTestingConsentMode;
        GuidanceMessage = guidanceMessage ?? (isTestingConsentMode ? TestingModeDefaultGuidance : ProductionModeDefaultGuidance);
    }
}
