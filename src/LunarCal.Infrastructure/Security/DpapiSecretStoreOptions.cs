namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Configuration options for <see cref="DpapiSecretStore"/> (SEC-KEY-1, SEC-OAUTH-3, ADR-0018).
/// </summary>
public sealed class DpapiSecretStoreOptions
{
    /// <summary>
    /// Custom directory path where encrypted secrets are persisted.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal/secrets'.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Optional custom application entropy. If null or empty, the default LunarCal entropy is used.
    /// </summary>
    public byte[]? Entropy { get; set; }
}
