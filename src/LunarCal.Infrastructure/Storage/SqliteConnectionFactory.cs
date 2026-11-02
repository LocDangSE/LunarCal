using System.Security.Cryptography;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Default implementation of <see cref="ISqliteConnectionFactory"/> backed by SQLCipher (SEC-DATA-1, ADR-0013).
/// Generates a random 256-bit encryption key on first run, wrapped via DPAPI in <see cref="ISecretStore"/>.
/// Automatically executes schema migrations upon initialization.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory, IDisposable
{
    static SqliteConnectionFactory()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly ISecretStore _secretStore;
    private readonly string _databasePath;
    private readonly string _secretKeyName;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string? _cachedKey;
    private bool _isInitialized;
    private bool _disposed;

    public SqliteConnectionFactory(
        ISecretStore secretStore,
        SqliteStorageOptions? options = null,
        SqliteMigrationRunner? migrationRunner = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _secretKeyName = !string.IsNullOrWhiteSpace(options?.SecretKeyName)
            ? options.SecretKeyName
            : KnownSecretKeys.DatabaseKey;

        _databasePath = ResolveDatabasePath(options?.DatabasePath);
        _migrationRunner = migrationRunner ?? new SqliteMigrationRunner();
    }

    /// <inheritdoc />
    public string DatabasePath => _databasePath;

    /// <inheritdoc />
    public async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            string key = await ResolveOrCreateDatabaseKeyAsync(cancellationToken).ConfigureAwait(false);
            _cachedKey = key;

            // Ensure directory exists if writing to disk
            if (!string.Equals(_databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                string? directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    LocalStorageDirectorySecurity.EnsureCurrentUserOnlyAcl(directory);
                }
            }

            // Apply migrations
            string connectionString = BuildConnectionString(key);
            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await _migrationRunner.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isInitialized)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }

        string connectionString = BuildConnectionString(_cachedKey!);
        return new SqliteConnection(connectionString);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initLock.Dispose();
    }

    private async Task<string> ResolveOrCreateDatabaseKeyAsync(CancellationToken cancellationToken)
    {
        string? existingKey = await _secretStore.GetSecretAsync(_secretKeyName, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existingKey))
        {
            return existingKey;
        }

        // If database file already exists on disk but key is missing from secret store, fail securely
        bool isDiskFile = !string.Equals(_databasePath, ":memory:", StringComparison.OrdinalIgnoreCase);
        if (isDiskFile && File.Exists(_databasePath))
        {
            throw new InvalidOperationException(
                $"Database file exists at '{_databasePath}' but encryption key is missing from secret store. Cannot open database.");
        }

        // Generate 256-bit (32 bytes) cryptographically random key (SEC-DATA-1)
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        string newKeyHex = Convert.ToHexString(keyBytes).ToLowerInvariant();

        await _secretStore.SetSecretAsync(_secretKeyName, newKeyHex, cancellationToken).ConfigureAwait(false);
        return newKeyHex;
    }

    private string BuildConnectionString(string password)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Password = password,
            ForeignKeys = true
        };

        return builder.ToString();
    }

    private static string ResolveDatabasePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LunarCal",
                "lunarcal.db");
        }

        if (string.Equals(configuredPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return ":memory:";
        }

        return Environment.ExpandEnvironmentVariables(configuredPath);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
