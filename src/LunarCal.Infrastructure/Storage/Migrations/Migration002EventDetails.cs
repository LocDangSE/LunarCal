using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage.Migrations;

/// <summary>
/// Migration V2 (P3-R1, P3-R2): adds the columns the calendar UI needs beyond the spec §6 minimum. Reminders and
/// recurrence of an event (FR-UI-4) and the access role and time zone of a calendar (read-only calendars cannot be
/// edited; all-day dates are kept in the calendar's zone).
/// </summary>
public sealed class Migration002EventDetails : ISqliteMigration
{
    /// <inheritdoc />
    public int Version => 2;

    /// <inheritdoc />
    public string Description => "Event reminders and recurrence; calendar access role and time zone";

    /// <inheritdoc />
    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        const string sql = """
            ALTER TABLE Event ADD COLUMN reminders_json TEXT;
            ALTER TABLE Event ADD COLUMN recurrence_json TEXT;
            ALTER TABLE CalendarInfo ADD COLUMN access_role TEXT;
            ALTER TABLE CalendarInfo ADD COLUMN time_zone TEXT;
            """;

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
