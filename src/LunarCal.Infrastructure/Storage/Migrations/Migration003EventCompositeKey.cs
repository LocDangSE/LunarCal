using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration V3 (P4-R3, spec §6, FR-SYNC-1): Changes the primary key of the Event table from `id` to the composite
/// `(calendar_id, id)` because Google event IDs are unique only within each calendar. Recreates the table,
/// preserves all existing data, foreign keys, and indexes.
/// </summary>
public sealed class Migration003EventCompositeKey : ISqliteMigration
{
    /// <inheritdoc />
    public int Version => 3;

    /// <inheritdoc />
    public string Description => "Change Event primary key to composite (calendar_id, id)";

    /// <inheritdoc />
    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        const string sql = """
            -- 1. Create new table with composite primary key (calendar_id, id)
            CREATE TABLE Event_new (
                id TEXT NOT NULL,
                calendar_id TEXT NOT NULL,
                google_id TEXT,
                recurring_event_id TEXT,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start_utc TEXT,
                end_utc TEXT,
                all_day INTEGER NOT NULL DEFAULT 0,
                timezone TEXT,
                etag TEXT,
                updated TEXT,
                status TEXT,
                extended_json TEXT,
                reminders_json TEXT,
                recurrence_json TEXT,
                PRIMARY KEY (calendar_id, id),
                FOREIGN KEY (calendar_id) REFERENCES CalendarInfo(id) ON DELETE CASCADE
            );

            -- 2. Migrate existing records from Event to Event_new
            INSERT INTO Event_new (
                id, calendar_id, google_id, recurring_event_id, title, description, location,
                start_utc, end_utc, all_day, timezone, etag, updated, status, extended_json, reminders_json, recurrence_json
            )
            SELECT
                id, calendar_id, google_id, recurring_event_id, title, description, location,
                start_utc, end_utc, all_day, timezone, etag, updated, status, extended_json, reminders_json, recurrence_json
            FROM Event;

            -- 3. Replace old table with new table
            DROP TABLE Event;
            ALTER TABLE Event_new RENAME TO Event;

            -- 4. Recreate index for range queries mandated by spec §6
            CREATE INDEX IF NOT EXISTS idx_event_calendar_start ON Event (calendar_id, start_utc);
            """;

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
