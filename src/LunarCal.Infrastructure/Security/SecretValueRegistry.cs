using System.Collections.Concurrent;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Thread-safe registry that stores runtime secret values (e.g. Gemini API keys) to dynamically
/// redact them from logs, exceptions, and user-facing messages by exact value match (ADR-0016, SEC-KEY-1).
/// Complements regex-based redaction when keys do not follow standard prefixes (like AIza...).
/// </summary>
public static class SecretValueRegistry
{
    private static readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a secret value and its URL-encoded variant to be redacted across all logging sinks.
    /// Values shorter than 4 characters or whitespace are ignored to avoid unintentional partial redactions.
    /// </summary>
    public static void Register(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 4)
        {
            return;
        }

        _secrets.TryAdd(secret, 0);

        string escaped = Uri.EscapeDataString(secret);
        if (!string.Equals(escaped, secret, StringComparison.Ordinal))
        {
            _secrets.TryAdd(escaped, 0);
        }
    }

    /// <summary>
    /// Unregisters a secret value and its URL-encoded variant when the secret is deleted or rotated.
    /// </summary>
    public static void Unregister(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        _secrets.TryRemove(secret, out _);

        string escaped = Uri.EscapeDataString(secret);
        _secrets.TryRemove(escaped, out _);
    }

    /// <summary>
    /// Clears all registered secrets (useful during tests and local data wipe).
    /// </summary>
    public static void Clear()
    {
        _secrets.Clear();
    }

    /// <summary>
    /// Checks whether any secrets are currently registered.
    /// </summary>
    public static bool HasSecrets => !_secrets.IsEmpty;

    /// <summary>
    /// Replaces occurrences of all registered secrets in the input string with the replacement marker.
    /// Evaluates longer secrets first to prevent partial replacements.
    /// </summary>
    public static string Redact(string? input, string replacement = LogRedactor.RedactedApiKey)
    {
        if (string.IsNullOrEmpty(input) || _secrets.IsEmpty)
        {
            return input ?? string.Empty;
        }

        var result = input;
        // Sort descending by length so longer strings match first
        foreach (var secret in _secrets.Keys.OrderByDescending(k => k.Length))
        {
            if (result.Contains(secret, StringComparison.Ordinal))
            {
                result = result.Replace(secret, replacement, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
