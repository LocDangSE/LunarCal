namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for securely persisting sensitive credentials and keys (spec §5.1, SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3, ADR-0018).
/// Implementations must encrypt data at rest (e.g. Windows DPAPI CurrentUser with app-specific entropy).
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Retrieves a secret string by key, or returns null if not found.
    /// </summary>
    ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a secret string under the specified key, encrypting it at rest.
    /// </summary>
    ValueTask SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves raw secret bytes by key, or returns null if not found.
    /// </summary>
    ValueTask<byte[]?> GetSecretBytesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists raw secret bytes under the specified key, encrypting them at rest.
    /// </summary>
    ValueTask SetSecretBytesAsync(string key, byte[] secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a secret exists for the given key.
    /// </summary>
    ValueTask<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a secret by key. Returns true if removed, false if not found.
    /// </summary>
    ValueTask<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all secrets stored in this store (SEC-DATA-3).
    /// </summary>
    ValueTask ClearAllAsync(CancellationToken cancellationToken = default);
}
