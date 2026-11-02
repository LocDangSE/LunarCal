using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Executes sequential schema migrations against SQLite database (spec §6, ADR-0013).
/// Records applied versions in '__schema_version' table to prevent re-execution.
/// </summary>
public sealed class SqliteMigrationRunner
{
    private readonly IReadOnlyList<ISqliteMigration> _migrations;

    public SqliteMigrationRunner(IEnumerable<ISqliteMigration>? migrations = null)
    {
        _migrations = migrations is not null
            ? migrations.OrderBy(m => m.Version).ToList()
            : [new Migration001InitialSchema()];
    }

    /// <summary>Gets the list of registered migrations.</summary>
    public IReadOnlyList<ISqliteMigration> Migrations => _migrations;

    /// <summary>
    /// Executes all unapplied migrations sequentially within database transactions.
    /// Returns the number of migrations applied during this run.
    /// </summary>
    public async Task<int> MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await EnsureSchemaVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);

        HashSet<int> appliedVersions = await GetAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);
        int appliedCount = 0;

        foreach (ISqliteMigration migration in _migrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                await migration.UpAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT INTO __schema_version (version, description, applied_at) VALUES (@version, @desc, @appliedAt);";
                    cmd.Parameters.AddWithValue("@version", migration.Version);
                    cmd.Parameters.AddWithValue("@desc", migration.Description);
                    cmd.Parameters.AddWithValue("@appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                transaction.Commit();
                appliedCount++;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        return appliedCount;
    }

    /// <summary>
    /// Returns the highest applied schema version, or 0 if no migrations have been applied.
    /// </summary>
    public static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await EnsureSchemaVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM __schema_version;";
        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task EnsureSchemaVersionTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS __schema_version (
                version INTEGER PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<int>> GetAppliedVersionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var versions = new HashSet<int>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM __schema_version;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }
}
