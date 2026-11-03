namespace LunarCal.Core.Abstractions;

/// <summary>
/// OAuth 2.0 tokens returned by Google's token endpoint (FR-AUTH-1, SEC-OAUTH-3).
/// </summary>
public sealed record GoogleOAuthTokens
{
    /// <summary>Short-lived access token for Google API calls.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Token type (typically "Bearer").</summary>
    public required string TokenType { get; init; }

    /// <summary>Lifetime of access token in seconds (typically 3600).</summary>
    public int ExpiresInSeconds { get; init; }

    /// <summary>Optional refresh token obtained with offline access.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Granted scopes delimited by spaces.</summary>
    public string? Scope { get; init; }

    /// <summary>Raw JSON Web Token (JWT) identity token.</summary>
    public string? IdToken { get; init; }

    /// <summary>Timestamp when tokens were issued.</summary>
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Calculates whether the access token has expired (with an optional safety buffer).</summary>
    public bool IsExpired(TimeSpan? safetyBuffer = null)
    {
        var buffer = safetyBuffer ?? TimeSpan.FromMinutes(1);
        return DateTimeOffset.UtcNow >= IssuedAt.AddSeconds(ExpiresInSeconds) - buffer;
    }
}

/// <summary>
/// Basic user information extracted from the authenticated Google account (spec §6 Account table).
/// </summary>
public sealed record GoogleUserInfo
{
    /// <summary>Unique Google user account ID ('sub' claim).</summary>
    public required string Id { get; init; }

    /// <summary>Google account email address.</summary>
    public required string Email { get; init; }

    /// <summary>User display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>User profile avatar URL.</summary>
    public string? PictureUrl { get; init; }
}

/// <summary>
/// Result of an interactive Google OAuth authorization attempt (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public sealed record GoogleAuthResult
{
    /// <summary>Whether authentication completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>User info if successful.</summary>
    public GoogleUserInfo? UserInfo { get; init; }

    /// <summary>OAuth tokens if successful.</summary>
    public GoogleOAuthTokens? Tokens { get; init; }

    /// <summary>Error code if failed (e.g. "access_denied", "invalid_state", "timeout").</summary>
    public string? Error { get; init; }

    /// <summary>Human-readable error description.</summary>
    public string? ErrorDescription { get; init; }

    /// <summary>Creates a successful authorization result.</summary>
    public static GoogleAuthResult Succeeded(GoogleUserInfo userInfo, GoogleOAuthTokens tokens) =>
        new()
        {
            Success = true,
            UserInfo = userInfo,
            Tokens = tokens
        };

    /// <summary>Creates a failed authorization result.</summary>
    public static GoogleAuthResult Failed(string error, string? description = null) =>
        new()
        {
            Success = false,
            Error = error,
            ErrorDescription = description
        };
}

/// <summary>
/// Client port for Google OAuth 2.0 authentication flows (FR-AUTH-1, FR-AUTH-3, SEC-OAUTH-1).
/// </summary>
public interface IGoogleOAuthClient
{
    /// <summary>
    /// Executes the interactive OAuth authorization code flow with PKCE S256 and loopback redirect (FR-AUTH-1, SEC-OAUTH-1).
    /// </summary>
    Task<GoogleAuthResult> AuthorizeAsync(CancellationToken cancellationToken = default);
}
