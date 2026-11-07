using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// <see cref="IEventStore"/> over the SQLCipher cache (spec §6, FR-SYNC-1, FR-SYNC-6, P3-R1). The calendar views
/// read the calendars and events the sync engine wrote, so the app works offline on the last synced state.
/// Range queries without a calendar filter return only events of calendars switched on in the sidebar (FR-UI-1).
/// </summary>
public sealed class SqliteEventStore : IEventStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    /// <summary>Creates the store.</summary>
    public SqliteEventStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {EventRows.CalendarColumns}
            FROM CalendarInfo c
            WHERE @account_id IS NULL OR c.account_id = @account_id;
            """;
        cmd.Parameters.AddWithValue("@account_id", (object?)accountId ?? DBNull.Value);

        var list = new List<CalendarInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(EventRows.ReadCalendar(reader));
        }

        // Same order as the sidebar in Figma 01: primary first, each account's calendars together, then by name.
        return list
            .OrderBy(c => !c.IsPrimary)
            .ThenBy(c => c.AccountId, StringComparer.Ordinal)
            .ThenBy(c => c.Summary, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CalendarInfo?> GetCalendarByIdAsync(string calendarId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {EventRows.CalendarColumns} FROM CalendarInfo c WHERE c.id = @id;";
        cmd.Parameters.AddWithValue("@id", calendarId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? EventRows.ReadCalendar(reader) : null;
    }

    /// <inheritdoc />
    /// <remarks>Used by the sidebar toggle: the <c>selected</c> flag written here is the user's choice and wins.</remarks>
    public async Task UpsertCalendarAsync(CalendarInfo calendar, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await EventRows.UpsertCalendarAsync(connection, null, calendar, overwriteSelected: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteCalendarAsync(string calendarId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Event WHERE calendar_id = @id; DELETE FROM CalendarInfo WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", calendarId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string? calendarId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {EventRows.EventColumns}
            FROM Event e
            JOIN CalendarInfo c ON c.id = e.calendar_id
            WHERE e.start_utc < @end AND e.end_utc > @start
              AND (e.status IS NULL OR e.status <> 'cancelled')
              AND ((@calendar_id IS NULL AND c.selected = 1) OR e.calendar_id = @calendar_id)
            ORDER BY e.start_utc, e.id;
            """;
        cmd.Parameters.AddWithValue("@start", EventRows.FormatInstant(startUtc));
        cmd.Parameters.AddWithValue("@end", EventRows.FormatInstant(endUtc));
        cmd.Parameters.AddWithValue("@calendar_id", (object?)calendarId ?? DBNull.Value);

        var list = new List<CalendarEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(EventRows.ReadEvent(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<CalendarEvent?> GetEventByIdAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {EventRows.EventColumns} FROM Event e WHERE e.id = @id OR e.google_id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", eventId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? EventRows.ReadEvent(reader) : null;
    }

    /// <inheritdoc />
    public async Task UpsertEventAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await EventRows.UpsertEventAsync(connection, null, calendarEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Event WHERE id = @id OR google_id = @id;";
        cmd.Parameters.AddWithValue("@id", eventId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
