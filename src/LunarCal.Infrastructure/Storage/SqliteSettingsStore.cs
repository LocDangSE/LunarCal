using System.Collections.Concurrent;
using System.Globalization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Persistent SQLCipher/SQLite implementation of <see cref="ISettingsStore"/> (spec §6 AppSetting, FR-UI-7, FR-LUNAR-6).
/// Automatically seeds application defaults into the database on first run if the table is empty.
/// Provides an in-memory dictionary fallback when running without a database connection factory.
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly ISqliteConnectionFactory? _connectionFactory;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, string> _fallbackSettings;
    private int _defaultsSeeded;

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteSettingsStore"/>.
    /// </summary>
    /// <param name="connectionFactory">Optional SQLite connection factory. If null, operates in fallback in-memory mode.</param>
    /// <param name="logger">Optional Serilog logger.</param>
    public SqliteSettingsStore(
        ISqliteConnectionFactory? connectionFactory = null,
        ILogger? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger ?? Log.ForContext<SqliteSettingsStore>();
        _fallbackSettings = new ConcurrentDictionary<string, string>(
            AppSettingDefaults.CreateInitialDefaults(),
            StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_connectionFactory is null)
        {
            _fallbackSettings.TryGetValue(key, out var fallbackValue);
            return fallbackValue;
        }

        await EnsureDefaultsSeededAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM AppSetting WHERE key = @key LIMIT 1;";
            command.Parameters.AddWithValue("@key", key);

            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is not null && scalar != DBNull.Value)
            {
                return (string)scalar;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read setting '{Key}' from SQLite AppSetting table.", key);
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_connectionFactory is null)
        {
            if (value is null)
            {
                _fallbackSettings.TryRemove(key, out _);
            }
            else
            {
                _fallbackSettings[key] = value;
            }

            return;
        }

        await EnsureDefaultsSeededAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();

            if (value is null)
            {
                command.CommandText = "DELETE FROM AppSetting WHERE key = @key;";
                command.Parameters.AddWithValue("@key", key);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                command.CommandText = """
                    INSERT INTO AppSetting (key, value)
                    VALUES (@key, @value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@value", value);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist setting '{Key}' to SQLite AppSetting table.", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionFactory is null)
        {
            var copy = new Dictionary<string, string>(_fallbackSettings, StringComparer.Ordinal);
            return copy;
        }

        await EnsureDefaultsSeededAsync(cancellationToken).ConfigureAwait(false);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM AppSetting;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var k = reader.GetString(0);
                var v = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                dict[k] = v;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read all settings from SQLite AppSetting table.");
        }

        return dict;
    }

    /// <inheritdoc />
    public async ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var raw = await GetStringAsync(key, cancellationToken).ConfigureAwait(false);
        if (raw is not null && bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    /// <inheritdoc />
    public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return SetStringAsync(key, value ? "true" : "false", cancellationToken);
    }

    private async ValueTask EnsureDefaultsSeededAsync(CancellationToken cancellationToken)
    {
        if (_connectionFactory is null || Interlocked.CompareExchange(ref _defaultsSeeded, 1, 0) != 0)
        {
            return;
        }

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM AppSetting;";
            var countObj = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var count = Convert.ToInt64(countObj, CultureInfo.InvariantCulture);

            if (count == 0)
            {
                var defaults = AppSettingDefaults.CreateInitialDefaults();
                using var tx = connection.BeginTransaction();
                foreach (var (key, value) in defaults)
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = "INSERT OR IGNORE INTO AppSetting (key, value) VALUES (@key, @value);";
                    insertCmd.Parameters.AddWithValue("@key", key);
                    insertCmd.Parameters.AddWithValue("@value", value);
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                tx.Commit();
                _logger.Information("Seeded {Count} initial settings into AppSetting table.", defaults.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to seed default settings into AppSetting table.");
        }
    }
}
