namespace LunarCal.Core.Abstractions;

/// <summary>
/// Reason for a network security policy violation (SEC-NET-1).
/// </summary>
public enum NetworkSecurityViolationReason
{
    /// <summary>
    /// The URI scheme is not HTTPS.
    /// </summary>
    InsecureScheme = 1,

    /// <summary>
    /// The destination host is not in the allowed hosts list.
    /// </summary>
    DisallowedHost = 2,

    /// <summary>
    /// The request URI is missing or invalid.
    /// </summary>
    InvalidUri = 3
}

/// <summary>
/// Exception thrown when an outbound HTTP request violates network security policies (SEC-NET-1).
/// Only HTTPS requests to explicitly allow-listed hosts are permitted.
/// </summary>
public sealed class NetworkSecurityException : Exception
{
    public Uri? BlockedUri { get; }
    public string? BlockedHost { get; }
    public NetworkSecurityViolationReason Reason { get; }

    public NetworkSecurityException(
        string message,
        NetworkSecurityViolationReason reason,
        Uri? blockedUri = null,
        string? blockedHost = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        BlockedUri = blockedUri;
        BlockedHost = blockedHost ?? blockedUri?.Host;
    }
}
