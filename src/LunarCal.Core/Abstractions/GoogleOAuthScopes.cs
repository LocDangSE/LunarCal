namespace LunarCal.Core.Abstractions;

/// <summary>
/// Well-known Google OAuth scopes for LunarCal (FR-AUTH-2, SEC-OAUTH-2).
/// Least privilege: only calendar events, calendar read-only, and OpenID user profile.
/// </summary>
public static class GoogleOAuthScopes
{
    /// <summary>Scope for reading and modifying calendar events.</summary>
    public const string CalendarEvents = "https://www.googleapis.com/auth/calendar.events";

    /// <summary>Scope for reading calendar list and metadata.</summary>
    public const string CalendarReadOnly = "https://www.googleapis.com/auth/calendar.readonly";

    /// <summary>OpenID connect scope for authenticating user identity.</summary>
    public const string OpenId = "openid";

    /// <summary>Scope for retrieving user email address.</summary>
    public const string UserInfoEmail = "https://www.googleapis.com/auth/userinfo.email";

    /// <summary>Scope for retrieving user basic profile (name, picture).</summary>
    public const string UserInfoProfile = "https://www.googleapis.com/auth/userinfo.profile";

    /// <summary>
    /// Default minimal scopes requested during authorization (FR-AUTH-2, SEC-OAUTH-2).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultScopes =
    [
        OpenId,
        UserInfoEmail,
        UserInfoProfile,
        CalendarEvents,
        CalendarReadOnly
    ];
}
