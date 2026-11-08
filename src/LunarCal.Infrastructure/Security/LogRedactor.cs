using System.Text.RegularExpressions;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Redacts sensitive credentials, tokens, event details, and personal data from log messages (SEC-LOG-1, SEC-LOG-2, FR-NOTIFY-9).
/// </summary>
public static partial class LogRedactor
{
    /// <summary>Redacted marker for Gemini API keys.</summary>
    public const string RedactedApiKey = "[REDACTED_API_KEY]";

    /// <summary>Redacted marker for OAuth access tokens.</summary>
    public const string RedactedAccessToken = "[REDACTED_ACCESS_TOKEN]";

    /// <summary>Redacted marker for Bearer tokens in headers or messages.</summary>
    public const string RedactedBearerToken = "[REDACTED_TOKEN]";

    /// <summary>Redacted marker for OAuth refresh tokens.</summary>
    public const string RedactedRefreshToken = "[REDACTED_REFRESH_TOKEN]";

    /// <summary>Redacted marker for OAuth client secrets.</summary>
    public const string RedactedClientSecret = "[REDACTED_CLIENT_SECRET]";

    /// <summary>Redacted marker for email addresses when redaction is enabled.</summary>
    public const string RedactedEmail = "[REDACTED_EMAIL]";

    /// <summary>Redacted marker for sensitive event fields (title, description, location).</summary>
    public const string RedactedEventField = "[REDACTED_EVENT_FIELD]";

    [GeneratedRegex(@"\bAIza[0-9A-Za-z\-_]{20,}\b")]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"\bya29\.[0-9A-Za-z\-_]+")]
    private static partial Regex AccessTokenRegex();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(authorization\s*:\s*(?:bearer|basic)\s+)[^\s,;]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"\b1//[0-9A-Za-z\-_]+")]
    private static partial Regex GoogleRefreshTokenRegex();

    [GeneratedRegex(@"(?i)([""']?(?:refresh_token|refreshtoken)[""']?\s*[:=]\s*[""']?)([^""'&\s,;]+)([""']?)")]
    private static partial Regex RefreshTokenKeyValueRegex();

    [GeneratedRegex(@"\bGOCSPX-[0-9A-Za-z\-_]+")]
    private static partial Regex ClientSecretPrefixRegex();

    [GeneratedRegex(@"(?i)([""']?(?:client_secret|clientsecret)[""']?\s*[:=]\s*[""']?)([^""'&\s,;]+)([""']?)")]
    private static partial Regex ClientSecretKeyValueRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailRegex();

    private static readonly HashSet<string> _sensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApiKey",
        "Key",
        "AccessToken",
        "Token",
        "RefreshToken",
        "ClientSecret",
        "Password",
        "Secret",
        "Authorization",
        "Title",
        "Summary",
        "Description",
        "Location",
        "EventTitle",
        "EventSummary",
        "EventDescription",
        "EventLocation"
    };

    /// <summary>
    /// Checks whether a property name is considered sensitive and must be redacted.
    /// </summary>
    public static bool IsSensitivePropertyName(string? propertyName)
    {
        return !string.IsNullOrEmpty(propertyName) && _sensitivePropertyNames.Contains(propertyName);
    }

    /// <summary>
    /// Redacts sensitive patterns (API keys, access tokens, bearer tokens, refresh tokens, client secrets, and optionally email) from text.
    /// </summary>
    public static string Redact(string? input, bool redactEmail = false)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var result = input;
        // 0. SecretValueRegistry exact-value matching (ADR-0016, SEC-KEY-1)
        result = SecretValueRegistry.Redact(result, RedactedApiKey);

        // 1. Authorization header: Authorization: Bearer ... or Authorization: Basic ...
        result = AuthorizationHeaderRegex().Replace(result, $"$1{RedactedBearerToken}");

        // 2. Bearer token pattern
        result = BearerTokenRegex().Replace(result, $"Bearer {RedactedBearerToken}");

        // 3. Gemini API key (AIza...)
        result = ApiKeyRegex().Replace(result, RedactedApiKey);

        // 4. Google access token (ya29...)
        result = AccessTokenRegex().Replace(result, RedactedAccessToken);

        // 5. Google refresh token (1//...)
        result = GoogleRefreshTokenRegex().Replace(result, RedactedRefreshToken);

        // 6. Key-value refresh token
        result = RefreshTokenKeyValueRegex().Replace(result, $"$1{RedactedRefreshToken}$3");

        // 7. Google client secret prefix (GOCSPX-...)
        result = ClientSecretPrefixRegex().Replace(result, RedactedClientSecret);

        // 8. Key-value client secret
        result = ClientSecretKeyValueRegex().Replace(result, $"$1{RedactedClientSecret}$3");

        // 9. Email (if configured)
        if (redactEmail)
        {
            result = EmailRegex().Replace(result, RedactedEmail);
        }

        return result;
    }
}
