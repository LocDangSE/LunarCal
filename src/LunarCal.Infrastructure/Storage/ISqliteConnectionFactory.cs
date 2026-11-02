using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Factory for creating secure, encrypted SQLite connections backed by SQLCipher (SEC-DATA-1, ADR-0013).
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Gets the filesystem path to the SQLite database.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Creates and returns a new <see cref="SqliteConnection"/> configured with the DPAPI-derived key.
    /// The caller is responsible for opening and disposing the connection.
    /// </summary>
    ValueTask<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures database encryption key exists in secret store, directory is created, and migrations are applied.
    /// </summary>
    ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
