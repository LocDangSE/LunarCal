using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration V1: Creates initial database schema containing all 9 tables defined in spec §6.
/// Includes indexed columns (calendar_id, start_utc) and composite primary keys.
/// </summary>
public sealed class Migration001InitialSchema : ISqliteMigration
{
    public int Version => 1;

    public string Description => "Initial schema with 9 tables per spec §6 and indexes";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        const string sql = """
            -- 1. Google account representation (FR-AUTH-5)
            CREATE TABLE IF NOT EXISTS Account (
                id TEXT PRIMARY KEY,
                email TEXT NOT NULL,
                display_name TEXT
            );

            -- 2. Calendar metadata & sync token
            CREATE TABLE IF NOT EXISTS CalendarInfo (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                summary TEXT NOT NULL,
                color TEXT,
                selected INTEGER NOT NULL DEFAULT 1,
                sync_token TEXT,
                FOREIGN KEY (account_id) REFERENCES Account(id) ON DELETE CASCADE
            );

            -- 3. Calendar event cache & sync state (spec §6, FR-SYNC-6)
            CREATE TABLE IF NOT EXISTS Event (
                id TEXT PRIMARY KEY,
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
                FOREIGN KEY (calendar_id) REFERENCES CalendarInfo(id) ON DELETE CASCADE
            );

            -- Index mandated by spec §6 for performant range queries
            CREATE INDEX IF NOT EXISTS idx_event_calendar_start ON Event (calendar_id, start_utc);

            -- 4. Offline recurring lunar anniversary rules (spec §4.4, §6)
            CREATE TABLE IF NOT EXISTS LunarRule (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                lunar_day INTEGER NOT NULL,
                lunar_month INTEGER NOT NULL,
                leap_policy TEXT NOT NULL,
                time TEXT,
                years_ahead INTEGER NOT NULL DEFAULT 5,
                calendar_id TEXT NOT NULL,
                reminders TEXT,
                created_at TEXT NOT NULL,
                last_materialized_until TEXT
            );

            -- 5. Outbound mutations queue when offline (FR-SYNC-4, NFR-REL)
            CREATE TABLE IF NOT EXISTS PendingMutation (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT
            );

            -- 6. Application key-value settings store (spec §6, FR-UI-7)
            CREATE TABLE IF NOT EXISTS AppSetting (
                key TEXT PRIMARY KEY,
                value TEXT
            );

            -- 7. AI command queue & history (spec §6, §7, FR-QUEUE)
            CREATE TABLE IF NOT EXISTS AiCommand (
                id TEXT PRIMARY KEY,
                display_no INTEGER NOT NULL,
                text TEXT NOT NULL,
                typed_at TEXT NOT NULL,
                timezone TEXT NOT NULL,
                input_lang TEXT NOT NULL,
                status TEXT NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                next_retry_at TEXT,
                last_error TEXT,
                batch_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            -- 8. Encrypted opt-in data lake outbox (spec §4.10, §6, FR-DATA-2)
            CREATE TABLE IF NOT EXISTS ContributionOutbox (
                id TEXT PRIMARY KEY,
                turn_id TEXT NOT NULL,
                payload_encrypted BLOB NOT NULL,
                schema_version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                uploaded_at TEXT,
                attempts INTEGER NOT NULL DEFAULT 0
            );

            -- 9. Snooze & completion state per occurrence (spec §6, FR-NOTIFY-6, 7)
            CREATE TABLE IF NOT EXISTS ReminderState (
                event_id TEXT NOT NULL,
                occurrence_start TEXT NOT NULL,
                snoozed_until TEXT,
                completed_at TEXT,
                last_fired_at TEXT,
                PRIMARY KEY (event_id, occurrence_start)
            );
            """;

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
