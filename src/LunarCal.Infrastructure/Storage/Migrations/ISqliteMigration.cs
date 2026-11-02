using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Contract for versioned SQLite schema migrations (spec §6, ADR-0013).
/// </summary>
public interface ISqliteMigration
{
    /// <summary>Unique sequential migration version number (e.g. 1, 2, ...).</summary>
    int Version { get; }

    /// <summary>Human-readable migration description.</summary>
    string Description { get; }

    /// <summary>Applies the migration within an active database transaction.</summary>
    Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default);
}
