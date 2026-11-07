using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Validates outbound HTTP requests against network security policies (SEC-NET-1).
/// Enforces HTTPS scheme and allow-listed host verification.
/// </summary>
public static class NetworkSecurityValidator
{
    /// <summary>
    /// Validates an outbound HTTP request against the configured network security options.
    /// Throws <see cref="NetworkSecurityException"/> if the request violates security policies.
    /// </summary>
    public static void ValidateRequest(HttpRequestMessage request, NetworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var uri = request.RequestUri;
        if (uri == null)
        {
            throw new NetworkSecurityException(
                "Request URI cannot be null (SEC-NET-1).",
                NetworkSecurityViolationReason.InvalidUri);
        }

        // SEC-NET-1: Enforce HTTPS scheme
        if (options.EnforceHttpsOnly && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new NetworkSecurityException(
                $"Insecure scheme '{uri.Scheme}' is blocked. Only HTTPS is allowed (SEC-NET-1).",
                NetworkSecurityViolationReason.InsecureScheme,
                blockedUri: uri);
        }

        // SEC-NET-1: Verify host against allowed hosts
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host) || !IsHostAllowed(host, options.AllowedHosts))
        {
            throw new NetworkSecurityException(
                $"Outbound request to host '{host}' was blocked because it is not in the allowed hosts list (SEC-NET-1).",
                NetworkSecurityViolationReason.DisallowedHost,
                blockedUri: uri,
                blockedHost: host);
        }
    }

    /// <summary>
    /// Checks whether the given host matches any pattern in the allowed patterns list.
    /// Supports exact matches and wildcard prefix patterns (e.g. "*.googleapis.com").
    /// </summary>
    public static bool IsHostAllowed(string host, IEnumerable<string> allowedPatterns)
    {
        if (string.IsNullOrWhiteSpace(host) || allowedPatterns == null)
        {
            return false;
        }

        var normalizedHost = host.Trim();

        foreach (var rawPattern in allowedPatterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
            {
                continue;
            }

            var pattern = rawPattern.Trim();

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..]; // e.g. ".googleapis.com"
                var baseDomain = pattern[2..]; // e.g. "googleapis.com"

                if (normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedHost, baseDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                if (string.Equals(normalizedHost, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
