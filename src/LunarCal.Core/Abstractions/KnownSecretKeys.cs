namespace LunarCal.Core.Abstractions;

/// <summary>
/// Well-known secret keys used across LunarCal (spec §5.1, SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3).
/// </summary>
public static class KnownSecretKeys
{
    /// <summary>Gemini BYOK API key (SEC-KEY-1, FR-BYOK-3).</summary>
    public const string GeminiApiKey = "gemini.api_key";

    /// <summary>Prefix for Google OAuth refresh tokens stored per account (SEC-OAUTH-3, FR-AUTH-3).</summary>
    public const string GoogleRefreshTokenPrefix = "google.refresh_token.";

    /// <summary>SQLCipher database encryption key (SEC-DATA-1).</summary>
    public const string DatabaseKey = "sqlite.database_key";

    /// <summary>Data lake contributor device encryption secret (P6a-06, SEC-CLOUD-2).</summary>
    public const string DataLakeDeviceSecret = "datalake.device_secret";

    /// <summary>Builds the secret key for a specific Google account refresh token.</summary>
    public static string GoogleRefreshToken(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return $"{GoogleRefreshTokenPrefix}{accountId}";
    }
}
