using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Encrypted secret storage backed by Windows DPAPI (<see cref="ProtectedData"/>) in <see cref="DataProtectionScope.CurrentUser"/>
/// scope with application-specific entropy (spec §5.1, SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3, ADR-0018).
/// </summary>
public sealed class DpapiSecretStore : ISecretStore, IDisposable
{
    private const byte PayloadVersion = 1;

    // Fixed application entropy to prevent unauthorized DPAPI decryption by other apps under the same user (ADR-0018).
    private static readonly byte[] _defaultEntropy = "LunarCal.AppSpecificEntropy.v1.2026-11-01"u8.ToArray();

    private readonly string _baseDirectory;
    private readonly byte[] _entropy;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="DpapiSecretStore"/> using default location (%LOCALAPPDATA%/LunarCal/secrets)
    /// and standard app entropy.
    /// </summary>
    public DpapiSecretStore()
        : this(baseDirectory: null, customEntropy: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DpapiSecretStore"/> with an explicit storage directory and optional entropy.
    /// </summary>
    public DpapiSecretStore(string? baseDirectory, byte[]? customEntropy = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarCal", "secrets")
            : baseDirectory;

        _entropy = customEntropy is { Length: > 0 }
            ? (byte[])customEntropy.Clone()
            : _defaultEntropy;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DpapiSecretStore"/> from options.
    /// </summary>
    public DpapiSecretStore(DpapiSecretStoreOptions options)
        : this(options?.BaseDirectory, options?.Entropy)
    {
    }

    /// <summary>Gets the base directory where secrets are stored.</summary>
    public string BaseDirectory => _baseDirectory;

    /// <inheritdoc />
    public async ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        byte[]? bytes = await GetSecretBytesAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <inheritdoc />
    public ValueTask SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        return SetSecretBytesAsync(key, bytes, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetSecretBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);

        string filePath = GetSecretFilePath(key);
        if (!File.Exists(filePath))
        {
            return null;
        }

        byte[] encrypted;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            encrypted = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        byte[] plainPayload = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);

        if (plainPayload.Length < 1 + sizeof(int))
        {
            throw new InvalidDataException("Corrupted secret payload: header too short.");
        }

        if (plainPayload[0] != PayloadVersion)
        {
            throw new InvalidDataException($"Unsupported secret payload version: {plainPayload[0]}.");
        }

        int keyLength = BinaryPrimitives.ReadInt32LittleEndian(plainPayload.AsSpan(1, sizeof(int)));
        if (keyLength < 0 || plainPayload.Length < 1 + sizeof(int) + keyLength)
        {
            throw new InvalidDataException("Corrupted secret payload: invalid key length.");
        }

        string storedKey = Encoding.UTF8.GetString(plainPayload.AsSpan(1 + sizeof(int), keyLength));
        if (!string.Equals(storedKey, key, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Secret payload key mismatch.");
        }

        return plainPayload.AsSpan(1 + sizeof(int) + keyLength).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask SetSecretBytesAsync(string key, byte[] secret, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);

        Storage.LocalStorageDirectorySecurity.EnsureCurrentUserOnlyAcl(_baseDirectory);
        string targetPath = GetSecretFilePath(key);

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] plainPayload = new byte[1 + sizeof(int) + keyBytes.Length + secret.Length];
        plainPayload[0] = PayloadVersion;
        BinaryPrimitives.WriteInt32LittleEndian(plainPayload.AsSpan(1, sizeof(int)), keyBytes.Length);
        keyBytes.CopyTo(plainPayload.AsSpan(1 + sizeof(int), keyBytes.Length));
        secret.CopyTo(plainPayload.AsSpan(1 + sizeof(int) + keyBytes.Length, secret.Length));

        byte[] encrypted = ProtectedData.Protect(plainPayload, _entropy, DataProtectionScope.CurrentUser);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string tempPath = Path.Combine(_baseDirectory, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);

        string filePath = GetSecretFilePath(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return File.Exists(filePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(key);

        string filePath = GetSecretFilePath(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask ClearAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_baseDirectory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(_baseDirectory, "*.dat"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Ignore transient file lock during cleanup
                }
            }

            foreach (string tempFile in Directory.EnumerateFiles(_baseDirectory, "*.tmp"))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                    // Ignore transient file lock during cleanup
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private string GetSecretFilePath(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string fileName = $"{Convert.ToHexString(hash).ToLowerInvariant()}.dat";
        return Path.Combine(_baseDirectory, fileName);
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
