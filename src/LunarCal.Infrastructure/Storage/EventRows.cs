using System.Globalization;
using System.Text.Json;
using LunarCal.Core.Calendar;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Single place that maps <see cref="CalendarEvent"/> and <see cref="CalendarInfo"/> to the SQLite rows of spec §6
/// (plus the V2 columns). The sync engine, the mutation service and the event store all write through here so the
/// three cannot drift apart (P3-R1).
/// </summary>
internal static class EventRows
{
    /// <summary>Column list for reading an <c>Event</c> row with <see cref="ReadEvent"/>.</summary>
    public const string EventColumns =
        "e.id, e.calendar_id, e.google_id, e.recurring_event_id, e.title, e.description, e.location, e.start_utc, e.end_utc, " +
        "e.all_day, e.timezone, e.etag, e.updated, e.status, e.extended_json, e.reminders_json, e.recurrence_json";

    /// <summary>Column list for reading a <c>CalendarInfo</c> row with <see cref="ReadCalendar"/>.</summary>
    public const string CalendarColumns =
        "c.id, c.account_id, c.summary, c.color, c.selected, c.sync_token, c.access_role, c.time_zone, " +
        "(SELECT COUNT(*) FROM Account a WHERE a.id = c.account_id AND a.email = c.id)";

    /// <summary>Inserts or replaces an event row.</summary>
    public static async Task UpsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CalendarEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(evt);

        const string sql = """
            INSERT INTO Event (
                id, calendar_id, google_id, recurring_event_id, title, description, location,
                start_utc, end_utc, all_day, timezone, etag, updated, status, extended_json, reminders_json, recurrence_json
            ) VALUES (
                @id, @calendar_id, @google_id, @recurring_event_id, @title, @description, @location,
                @start_utc, @end_utc, @all_day, @timezone, @etag, @updated, @status, @extended_json, @reminders_json, @recurrence_json
            )
            ON CONFLICT(calendar_id, id) DO UPDATE SET
                google_id = excluded.google_id,
                recurring_event_id = excluded.recurring_event_id,
                title = excluded.title,
                description = excluded.description,
                location = excluded.location,
                start_utc = excluded.start_utc,
                end_utc = excluded.end_utc,
                all_day = excluded.all_day,
                timezone = excluded.timezone,
                etag = excluded.etag,
                updated = excluded.updated,
                status = excluded.status,
                extended_json = excluded.extended_json,
                reminders_json = excluded.reminders_json,
                recurrence_json = excluded.recurrence_json;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", evt.Id);
        cmd.Parameters.AddWithValue("@calendar_id", evt.CalendarId);
        cmd.Parameters.AddWithValue("@google_id", (object?)evt.GoogleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recurring_event_id", (object?)evt.RecurringEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", evt.Title);
        cmd.Parameters.AddWithValue("@description", (object?)evt.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@location", (object?)evt.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@start_utc", FormatInstant(evt.StartUtc));
        cmd.Parameters.AddWithValue("@end_utc", FormatInstant(evt.EndUtc));
        cmd.Parameters.AddWithValue("@all_day", evt.AllDay ? 1 : 0);
        cmd.Parameters.AddWithValue("@timezone", (object?)evt.TimeZone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@etag", (object?)evt.Etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@updated", evt.Updated.HasValue ? FormatInstant(evt.Updated.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)evt.Status ?? "confirmed");
        cmd.Parameters.AddWithValue("@extended_json", evt.ExtendedProperties.Count > 0 ? JsonSerializer.Serialize(evt.ExtendedProperties) : DBNull.Value);
        cmd.Parameters.AddWithValue("@reminders_json", evt.ReminderMinutes is null ? DBNull.Value : JsonSerializer.Serialize(evt.ReminderMinutes));
        cmd.Parameters.AddWithValue("@recurrence_json", evt.Recurrence is { Count: > 0 } ? JsonSerializer.Serialize(evt.Recurrence) : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a calendar row. The local <c>selected</c> flag (sidebar toggle) is kept when the calendar
    /// already exists, so a sync from Google does not undo the user's choice; a null sync token keeps the stored one.
    /// </summary>
    public static async Task UpsertCalendarAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CalendarInfo calendar,
        bool overwriteSelected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(calendar);

        var sql = $"""
            INSERT INTO CalendarInfo (id, account_id, summary, color, selected, sync_token, access_role, time_zone)
            VALUES (@id, @account_id, @summary, @color, @selected, @sync_token, @access_role, @time_zone)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                summary = excluded.summary,
                color = excluded.color,
                selected = {(overwriteSelected ? "excluded.selected" : "CalendarInfo.selected")},
                sync_token = COALESCE(excluded.sync_token, CalendarInfo.sync_token),
                access_role = COALESCE(excluded.access_role, CalendarInfo.access_role),
                time_zone = COALESCE(excluded.time_zone, CalendarInfo.time_zone);
            """;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", calendar.Id);
        cmd.Parameters.AddWithValue("@account_id", calendar.AccountId);
        cmd.Parameters.AddWithValue("@summary", calendar.Summary);
        cmd.Parameters.AddWithValue("@color", (object?)calendar.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@selected", calendar.IsSelected ? 1 : 0);
        cmd.Parameters.AddWithValue("@sync_token", (object?)calendar.SyncToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@access_role", calendar.IsReadOnly ? "reader" : "writer");
        cmd.Parameters.AddWithValue("@time_zone", (object?)calendar.TimeZone ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads an event from a row selected with <see cref="EventColumns"/>.</summary>
    public static CalendarEvent ReadEvent(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new CalendarEvent
        {
            Id = reader.GetString(0),
            CalendarId = reader.GetString(1),
            GoogleId = NullableString(reader, 2),
            RecurringEventId = NullableString(reader, 3),
            Title = reader.GetString(4),
            Description = NullableString(reader, 5),
            Location = NullableString(reader, 6),
            StartUtc = ParseInstant(reader.GetString(7)),
            EndUtc = ParseInstant(reader.GetString(8)),
            AllDay = reader.GetInt32(9) == 1,
            TimeZone = NullableString(reader, 10),
            Etag = NullableString(reader, 11),
            Updated = NullableString(reader, 12) is { } updated ? ParseInstant(updated) : null,
            Status = NullableString(reader, 13),
            ExtendedProperties = NullableString(reader, 14) is { } ext
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(ext) ?? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            ReminderMinutes = NullableString(reader, 15) is { } rem ? JsonSerializer.Deserialize<List<int>>(rem) : null,
            Recurrence = NullableString(reader, 16) is { } rec ? JsonSerializer.Deserialize<List<string>>(rec) : null,
        };
    }

    /// <summary>Reads a calendar from a row selected with <see cref="CalendarColumns"/>.</summary>
    public static CalendarInfo ReadCalendar(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var accessRole = NullableString(reader, 6);
        return new CalendarInfo
        {
            Id = reader.GetString(0),
            AccountId = reader.GetString(1),
            Summary = reader.GetString(2),
            Color = NullableString(reader, 3),
            IsSelected = reader.GetInt32(4) == 1,
            SyncToken = NullableString(reader, 5),
            IsReadOnly = string.Equals(accessRole, "reader", StringComparison.Ordinal),
            TimeZone = NullableString(reader, 7),
            IsPrimary = reader.GetInt64(8) > 0,
        };
    }

    /// <summary>Formats an instant the way every query compares it: UTC, fixed width, so text order = time order.</summary>
    public static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
