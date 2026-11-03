using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Cryptographic state token generation and validation to prevent Cross-Site Request Forgery (CSRF) in OAuth flows (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public static class OAuthState
{
    /// <summary>
    /// Generates a cryptographically secure random state string using 32 bytes of entropy.
    /// </summary>
    public static string Generate()
    {
        Span<byte> randomBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        return Base64Url.EncodeToString(randomBytes);
    }

    /// <summary>
    /// Validates that the received state parameter matches the expected state parameter.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    public static bool Validate(string expected, string? received)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var receivedBytes = Encoding.UTF8.GetBytes(received);

        if (expectedBytes.Length != receivedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
    }
}
