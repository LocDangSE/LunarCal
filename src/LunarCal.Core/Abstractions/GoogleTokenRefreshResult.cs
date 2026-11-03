namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of an OAuth token refresh attempt (FR-AUTH-3, FR-AUTH-6).
/// </summary>
public sealed record GoogleTokenRefreshResult
{
    /// <summary>Whether token refresh succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Refreshed OAuth tokens if successful.</summary>
    public GoogleOAuthTokens? Tokens { get; init; }

    /// <summary>Whether the user must re-authenticate (e.g. invalid_grant or missing token).</summary>
    public bool RequiresReauthentication { get; init; }

    /// <summary>Whether Google returned an 'invalid_grant' error.</summary>
    public bool IsInvalidGrant { get; init; }

    /// <summary>Error code (e.g. "invalid_grant", "missing_refresh_token", "missing_client_id", "network_error").</summary>
    public string? Error { get; init; }

    /// <summary>Description returned by server or client.</summary>
    public string? ErrorDescription { get; init; }

    /// <summary>Actionable guidance message for the user, including 7-day Testing mode warning if applicable (FR-AUTH-6).</summary>
    public string? GuidanceMessage { get; init; }

    /// <summary>Creates a successful token refresh result.</summary>
    public static GoogleTokenRefreshResult Succeeded(GoogleOAuthTokens tokens) =>
        new()
        {
            Success = true,
            Tokens = tokens,
            RequiresReauthentication = false,
            IsInvalidGrant = false
        };

    /// <summary>Creates a failed token refresh result.</summary>
    public static GoogleTokenRefreshResult Failed(
        string error,
        string? description = null,
        bool requiresReauthentication = false,
        bool isInvalidGrant = false,
        string? guidanceMessage = null) =>
        new()
        {
            Success = false,
            Error = error,
            ErrorDescription = description,
            RequiresReauthentication = requiresReauthentication,
            IsInvalidGrant = isInvalidGrant,
            GuidanceMessage = guidanceMessage
        };
}
