using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Configuration options for Google OAuth 2.0 authentication (FR-AUTH-1, FR-AUTH-6, SEC-OAUTH-1).
/// </summary>
public sealed class GoogleOAuthOptions
{
    /// <summary>
    /// Google OAuth Client ID for desktop application (FR-AUTH-6).
    /// Loaded from configuration or user-secrets (SEC-KEY-5).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional OAuth Client Secret (ADR-0017, SEC-KEY-5).
    /// Loaded from user-secrets in development or environment variable in CI.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Timeout for loopback listener in seconds. Defaults to 300 seconds (5 minutes, SEC-OAUTH-1).
    /// </summary>
    public int LoopbackTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// List of OAuth scopes to request. Defaults to minimal required scopes (FR-AUTH-2, SEC-OAUTH-2).
    /// </summary>
    public IReadOnlyList<string> Scopes { get; set; } = GoogleOAuthScopes.DefaultScopes;

    /// <summary>Google OAuth 2.0 authorization endpoint.</summary>
    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>Google OAuth 2.0 token endpoint.</summary>
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>Google OAuth 2.0 token revocation endpoint.</summary>
    public string RevokeEndpoint { get; set; } = "https://oauth2.googleapis.com/revoke";

    /// <summary>Google OpenID Connect userinfo endpoint (fallback for profile discovery).</summary>
    public string UserInfoEndpoint { get; set; } = "https://www.googleapis.com/oauth2/v3/userinfo";
}
