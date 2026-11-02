using System.IO;
using LunarCal.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Service for wiping all local application data (cache, tokens, keys, outbox) per SEC-DATA-3.
/// </summary>
public sealed class LocalDataWipeService : ILocalDataWipeService
{
    private static readonly ILogger _logger = Log.ForContext<LocalDataWipeService>();
    private readonly ISecretStore? _secretStore;
    private readonly LocalDataWipeOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalDataWipeService"/>.
    /// </summary>
    /// <param name="secretStore">Optional secret store instance to clear secrets from.</param>
    /// <param name="options">Optional configuration options for resolving file paths.</param>
    public LocalDataWipeService(
        ISecretStore? secretStore = null,
        LocalDataWipeOptions? options = null)
    {
        _secretStore = secretStore;
        _options = options ?? new LocalDataWipeOptions();
    }

    /// <inheritdoc />
    public async Task WipeAllDataAsync(CancellationToken cancellationToken = default)
    {
        string rootDirectory = ResolveRootDirectory();
        string databasePath = ResolveDatabasePath(rootDirectory);
        string secretsDirectory = ResolveSecretsDirectory(rootDirectory);
        string dataLakeDirectory = ResolveDataLakeDirectory(rootDirectory);

        _logger.Information("Starting full wipe of local application data (SEC-DATA-3). Root directory: {RootDirectory}", rootDirectory);

        // 1. Clear DPAPI secret store (tokens, Gemini API key, database encryption key)
        if (_secretStore != null)
        {
            try
            {
                await _secretStore.ClearAllAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to clear secrets via secret store interface.");
            }
        }

        if (Directory.Exists(secretsDirectory))
        {
            try
            {
                Directory.Delete(secretsDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete secrets directory '{SecretsDirectory}'.", secretsDirectory);
            }
        }

        // 2. Clear SQLite connection pools and delete database files
        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to clear SQLite connection pools.");
        }

        DeleteDatabaseFiles(databasePath);

        // 3. Delete data lake outbox files and directories
        if (Directory.Exists(dataLakeDirectory))
        {
            try
            {
                Directory.Delete(dataLakeDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete data lake directory '{DataLakeDirectory}'.", dataLakeDirectory);
            }
        }

        // 4. Clean remaining files and subdirectories in root directory (except locked active logs)
        if (Directory.Exists(rootDirectory))
        {
            CleanRootDirectory(rootDirectory);

            // 5. Ensure ACL on root directory remains restricted to current user (SEC-DATA-2)
            try
            {
                LocalStorageDirectorySecurity.EnsureCurrentUserOnlyAcl(rootDirectory);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to re-apply ACL on root directory '{RootDirectory}'.", rootDirectory);
            }
        }

        _logger.Information("Completed full wipe of local application data.");
    }

    private string ResolveRootDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.RootDirectory))
        {
            return Path.GetFullPath(_options.RootDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LunarCal");
    }

    private string ResolveDatabasePath(string rootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            return Path.GetFullPath(_options.DatabasePath);
        }

        return Path.Combine(rootDirectory, "lunarcal.db");
    }

    private string ResolveSecretsDirectory(string rootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(_options.SecretsDirectory))
        {
            return Path.GetFullPath(_options.SecretsDirectory);
        }

        return Path.Combine(rootDirectory, "secrets");
    }

    private string ResolveDataLakeDirectory(string rootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(_options.DataLakeDirectory))
        {
            return Path.GetFullPath(_options.DataLakeDirectory);
        }

        return Path.Combine(rootDirectory, "datalake");
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        TryDeleteFile(databasePath);
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
        TryDeleteFile(databasePath + "-journal");

        string? dbDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dbDir) && Directory.Exists(dbDir))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(dbDir, "*.db*"))
                {
                    TryDeleteFile(file);
                }
            }
            catch (IOException)
            {
                // Ignore transient file enumerate errors
            }
        }
    }

    private static void CleanRootDirectory(string rootDirectory)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(rootDirectory))
            {
                TryDeleteFile(file);
            }

            foreach (string subDir in Directory.EnumerateDirectories(rootDirectory))
            {
                try
                {
                    Directory.Delete(subDir, recursive: true);
                }
                catch (IOException)
                {
                    // Locked sub-directory (e.g. active Serilog logger) is ignored safely
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore access issues for locked items
                }
            }
        }
        catch (Exception)
        {
            // Transient directory access issues
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // File might be held by another process; ignored safely
        }
        catch (UnauthorizedAccessException)
        {
            // File might be read-only or permission restricted
        }
    }
}
