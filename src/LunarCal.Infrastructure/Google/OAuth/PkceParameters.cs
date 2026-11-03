using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Implements RFC 7636 Proof Key for Code Exchange (PKCE) with S256 (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public sealed class PkceParameters
{
    /// <summary>Code challenge method (always "S256" per SEC-OAUTH-1).</summary>
    public const string MethodS256 = "S256";

    /// <summary>
    /// Cryptographically secure random code verifier (43 to 128 characters).
    /// </summary>
    public string CodeVerifier { get; }

    /// <summary>
    /// Base64URL-encoded SHA-256 hash of the code verifier.
    /// </summary>
    public string CodeChallenge { get; }

    /// <summary>
    /// Code challenge method ("S256").
    /// </summary>
    public static string CodeChallengeMethod => MethodS256;

    private PkceParameters(string codeVerifier, string codeChallenge)
    {
        CodeVerifier = codeVerifier;
        CodeChallenge = codeChallenge;
    }

    /// <summary>
    /// Generates a new PKCE parameter pair using 32 bytes of cryptographically secure random entropy (RFC 7636).
    /// </summary>
    public static PkceParameters Create()
    {
        Span<byte> randomBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var verifier = Base64Url.EncodeToString(randomBytes);

        var challenge = ComputeChallenge(verifier);
        return new PkceParameters(verifier, challenge);
    }

    /// <summary>
    /// Creates PKCE parameters from a pre-defined verifier (useful for testing).
    /// </summary>
    public static PkceParameters FromVerifier(string codeVerifier)
    {
        ValidateVerifier(codeVerifier);
        var challenge = ComputeChallenge(codeVerifier);
        return new PkceParameters(codeVerifier, challenge);
    }

    /// <summary>
    /// Computes the S256 code challenge for a given code verifier.
    /// </summary>
    public static string ComputeChallenge(string codeVerifier)
    {
        ValidateVerifier(codeVerifier);
        var verifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(verifierBytes, hash);
        return Base64Url.EncodeToString(hash);
    }

    /// <summary>
    /// Validates that a code verifier conforms to RFC 7636 section 4.1.
    /// </summary>
    public static void ValidateVerifier(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        if (codeVerifier.Length is < 43 or > 128)
        {
            throw new ArgumentException(
                $"Code verifier length must be between 43 and 128 characters (was {codeVerifier.Length}).",
                nameof(codeVerifier));
        }

        foreach (var c in codeVerifier)
        {
            var isValidChar = (c is >= 'A' and <= 'Z') ||
                              (c is >= 'a' and <= 'z') ||
                              (c is >= '0' and <= '9') ||
                              c is '-' or '.' or '_' or '~';

            if (!isValidChar)
            {
                throw new ArgumentException(
                    $"Code verifier contains invalid character '{c}'. Only unreserved RFC 7636 characters are permitted.",
                    nameof(codeVerifier));
            }
        }
    }
}
