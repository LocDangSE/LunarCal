using System.Globalization;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Default implementation of <see cref="ISyncEngine"/> coordinating Google Calendar API calls
/// and SQLite caching (FR-SYNC-1, FR-SYNC-2, FR-SYNC-6, ADR-0028).
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private sealed record CalendarIncrementalData(
        CalendarInfo Calendar,
        IReadOnlyList<CalendarEvent> Events,
        bool IsFullResync);

    private readonly ICalendarGateway _gateway;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public SyncEngine(
        ICalendarGateway gateway,
        IGoogleOAuthClient oauthClient,
        ISqliteConnectionFactory connectionFactory,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? Log.ForContext<SyncEngine>();
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncInitialAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        _logger.Information("Starting initial sync for account {AccountId} (FR-SYNC-1)", accountId);

        // 1. Acquire valid access token via OAuth client (refreshes expired token automatically per FR-AUTH-3)
        var refreshResult = await _oauthClient.TryRefreshAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
        var accessToken = refreshResult.Tokens?.AccessToken;
        if (!refreshResult.Success || string.IsNullOrWhiteSpace(accessToken))
        {
            var msg = refreshResult.GuidanceMessage ?? "Failed to acquire valid access token";
            _logger.Warning("Initial sync aborted for account {AccountId}: {Reason}", accountId, msg);
            return SyncResult.Failed(msg, requiresReauth: refreshResult.RequiresReauthentication);
        }

        // 2. Fetch user's calendar list (FR-SYNC-1)
        var calendars = new List<CalendarInfo>();
        string? calPageToken = null;

        try
        {
            do
            {
                var listResult = await _gateway.GetCalendarListAsync(
                    accessToken,
                    calPageToken,
                    syncToken: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var cal in listResult.Calendars)
                {
                    calendars.Add(cal with { AccountId = accountId });
                }

                calPageToken = listResult.NextPageToken;
            } while (!string.IsNullOrEmpty(calPageToken));
        }
        catch (GoogleCalendarSyncTokenExpiredException)
        {
            return SyncResult.Failed("Calendar list sync token expired", needsFullResync: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch calendar list for account {AccountId}", accountId);
            return SyncResult.Failed($"Failed to fetch calendar list: {ex.Message}");
        }

        // 3. Fetch events for each calendar within window [-12 months, +24 months] (FR-SYNC-1)
        var calendarDataList = new List<(CalendarInfo Calendar, string? NextSyncToken, List<CalendarEvent> Events)>();
        var totalEventsSynced = 0;

        try
        {
            foreach (var cal in calendars)
            {
                var events = new List<CalendarEvent>();
                string? nextSyncToken = null;

                if (cal.IsSelected)
                {
                    (events, nextSyncToken) = await FetchFullCalendarEventsAsync(accessToken, cal.Id, cancellationToken).ConfigureAwait(false);
                }

                totalEventsSynced += events.Count;
                calendarDataList.Add((cal with { SyncToken = nextSyncToken ?? cal.SyncToken }, nextSyncToken, events));
            }
        }
        catch (GoogleCalendarSyncTokenExpiredException)
        {
            return SyncResult.Failed("Events sync token expired", needsFullResync: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch events from Google for account {AccountId}", accountId);
            return SyncResult.Failed($"Failed to fetch events from Google: {ex.Message}");
        }

        // 4. Atomically persist calendars and events into SQLite database (FR-SYNC-6)
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureAccountExistsAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

            foreach (var (calendar, _, events) in calendarDataList)
            {
                await UpsertCalendarInfoAsync(connection, transaction, calendar, cancellationToken).ConfigureAwait(false);

                foreach (var evt in events)
                {
                    await UpsertEventAsync(connection, transaction, evt, cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.Information("Initial sync completed: {CalendarCount} calendars, {EventCount} events for account {AccountId}",
                calendars.Count, totalEventsSynced, accountId);

            return SyncResult.Succeeded(calendars.Count, totalEventsSynced);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Initial sync failed and rolled back for account {AccountId}", accountId);
            return SyncResult.Failed($"Initial sync database write failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncIncrementalAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        _logger.Information("Starting incremental sync for account {AccountId} (FR-SYNC-2)", accountId);

        // 1. Acquire valid access token via OAuth client (refreshes expired token automatically per FR-AUTH-3)
        var refreshResult = await _oauthClient.TryRefreshAccessTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
        var accessToken = refreshResult.Tokens?.AccessToken;
        if (!refreshResult.Success || string.IsNullOrWhiteSpace(accessToken))
        {
            var msg = refreshResult.GuidanceMessage ?? "Failed to acquire valid access token";
            _logger.Warning("Incremental sync aborted for account {AccountId}: {Reason}", accountId, msg);
            return SyncResult.Failed(msg, requiresReauth: refreshResult.RequiresReauthentication);
        }

        // 2. Open DB connection and load existing calendars for this account
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var existingCalendars = await LoadCalendarsForAccountAsync(connection, accountId, cancellationToken).ConfigureAwait(false);
        if (existingCalendars.Count == 0)
        {
            _logger.Information("No local calendars found for account {AccountId}. Falling back to initial sync.", accountId);
            return await SyncInitialAsync(accountId, cancellationToken).ConfigureAwait(false);
        }

        // 3. Process each calendar
        var calendarDeltas = new List<CalendarIncrementalData>();
        var totalEventsAffected = 0;
        var fullResyncTriggered = false;

        foreach (var cal in existingCalendars)
        {
            if (!cal.IsSelected)
            {
                continue;
            }

            // If this calendar has no sync token yet, perform full sync for this calendar
            if (string.IsNullOrWhiteSpace(cal.SyncToken))
            {
                _logger.Information("Calendar {CalendarId} has no sync token. Performing full events fetch.", cal.Id);
                var (fullEvents, nextSync) = await FetchFullCalendarEventsAsync(accessToken, cal.Id, cancellationToken).ConfigureAwait(false);
                calendarDeltas.Add(new CalendarIncrementalData(cal with { SyncToken = nextSync }, fullEvents, IsFullResync: true));
                continue;
            }

            // Incremental fetch with syncToken
            try
            {
                var deltaEvents = new List<CalendarEvent>();
                string? pageToken = null;
                string? nextSyncToken = null;

                do
                {
                    var eventsResult = await _gateway.GetEventsAsync(
                        accessToken,
                        cal.Id,
                        singleEvents: true,
                        timeMin: null,
                        timeMax: null,
                        pageToken: pageToken,
                        syncToken: cal.SyncToken,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    deltaEvents.AddRange(eventsResult.Events);
                    pageToken = eventsResult.NextPageToken;
                    if (!string.IsNullOrEmpty(eventsResult.NextSyncToken))
                    {
                        nextSyncToken = eventsResult.NextSyncToken;
                    }
                } while (!string.IsNullOrEmpty(pageToken));

                calendarDeltas.Add(new CalendarIncrementalData(
                    cal with { SyncToken = nextSyncToken ?? cal.SyncToken },
                    deltaEvents,
                    IsFullResync: false));
            }
            catch (GoogleCalendarSyncTokenExpiredException)
            {
                // HTTP 410 Gone: sync token expired -> trigger full resync for this calendar (FR-SYNC-2)
                _logger.Warning("Sync token expired (HTTP 410) for calendar {CalendarId}. Resetting sync token and triggering full resync (FR-SYNC-2).", cal.Id);
                fullResyncTriggered = true;

                var (freshEvents, freshSyncToken) = await FetchFullCalendarEventsAsync(accessToken, cal.Id, cancellationToken).ConfigureAwait(false);
                calendarDeltas.Add(new CalendarIncrementalData(
                    cal with { SyncToken = freshSyncToken },
                    freshEvents,
                    IsFullResync: true));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch incremental events for calendar {CalendarId}", cal.Id);
                return SyncResult.Failed($"Incremental sync failed for calendar {cal.Id}: {ex.Message}");
            }
        }

        // 4. Persist deltas into SQLite within an atomic transaction
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureAccountExistsAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

            foreach (var delta in calendarDeltas)
            {
                await UpsertCalendarInfoAsync(connection, transaction, delta.Calendar, cancellationToken).ConfigureAwait(false);

                if (delta.IsFullResync)
                {
                    // Purge old events for this calendar to ensure clean replacement after full resync
                    await DeleteAllEventsForCalendarAsync(connection, transaction, delta.Calendar.Id, cancellationToken).ConfigureAwait(false);

                    foreach (var evt in delta.Events)
                    {
                        if (!string.Equals(evt.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            await UpsertEventAsync(connection, transaction, evt, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    foreach (var evt in delta.Events)
                    {
                        if (string.Equals(evt.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            await DeleteEventAsync(connection, transaction, delta.Calendar.Id, evt.Id, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await UpsertEventAsync(connection, transaction, evt, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                totalEventsAffected += delta.Events.Count;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.Information("Incremental sync completed: {CalendarCount} calendars, {EventCount} events for account {AccountId} (fullResyncTriggered={FullResync})",
                calendarDeltas.Count, totalEventsAffected, accountId, fullResyncTriggered);

            return SyncResult.Succeeded(calendarDeltas.Count, totalEventsAffected, fullResyncTriggered);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Incremental sync database write failed and rolled back for account {AccountId}", accountId);
            return SyncResult.Failed($"Incremental sync database write failed: {ex.Message}");
        }
    }

    private async Task<(List<CalendarEvent> Events, string? NextSyncToken)> FetchFullCalendarEventsAsync(
        string accessToken,
        string calendarId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var timeMin = now.AddMonths(-12);
        var timeMax = now.AddMonths(24);

        var events = new List<CalendarEvent>();
        string? pageToken = null;
        string? nextSyncToken = null;

        do
        {
            var eventsResult = await _gateway.GetEventsAsync(
                accessToken,
                calendarId,
                singleEvents: true,
                timeMin: timeMin,
                timeMax: timeMax,
                pageToken: pageToken,
                syncToken: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            events.AddRange(eventsResult.Events);
            pageToken = eventsResult.NextPageToken;
            if (!string.IsNullOrEmpty(eventsResult.NextSyncToken))
            {
                nextSyncToken = eventsResult.NextSyncToken;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        return (events, nextSyncToken);
    }

    private static async Task<List<CalendarInfo>> LoadCalendarsForAccountAsync(
        SqliteConnection connection,
        string accountId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT id, account_id, summary, color, selected, sync_token
            FROM CalendarInfo
            WHERE account_id = @account_id;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@account_id", accountId);

        var list = new List<CalendarInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new CalendarInfo
            {
                Id = reader.GetString(0),
                AccountId = reader.GetString(1),
                Summary = reader.GetString(2),
                Color = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsSelected = reader.GetInt32(4) == 1,
                SyncToken = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return list;
    }

    private static async Task DeleteEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string calendarId,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM Event WHERE (id = @id OR google_id = @id) AND calendar_id = @calendar_id;";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@calendar_id", calendarId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteAllEventsForCalendarAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string calendarId,
        CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM Event WHERE calendar_id = @calendar_id;";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@calendar_id", calendarId);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureAccountExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO Account (id, email, display_name)
            VALUES (@id, @email, @display_name)
            ON CONFLICT(id) DO NOTHING;";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", accountId);
        cmd.Parameters.AddWithValue("@email", accountId.Contains('@') ? accountId : $"{accountId}@google.account");
        cmd.Parameters.AddWithValue("@display_name", DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCalendarInfoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CalendarInfo calendar,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO CalendarInfo (id, account_id, summary, color, selected, sync_token)
            VALUES (@id, @account_id, @summary, @color, @selected, @sync_token)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                summary = excluded.summary,
                color = excluded.color,
                selected = excluded.selected,
                sync_token = COALESCE(excluded.sync_token, CalendarInfo.sync_token);";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", calendar.Id);
        cmd.Parameters.AddWithValue("@account_id", calendar.AccountId);
        cmd.Parameters.AddWithValue("@summary", calendar.Summary);
        cmd.Parameters.AddWithValue("@color", (object?)calendar.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@selected", calendar.IsSelected ? 1 : 0);
        cmd.Parameters.AddWithValue("@sync_token", (object?)calendar.SyncToken ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CalendarEvent evt,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO Event (
                id, calendar_id, google_id, recurring_event_id, title, description, location,
                start_utc, end_utc, all_day, timezone, etag, updated, status, extended_json
            ) VALUES (
                @id, @calendar_id, @google_id, @recurring_event_id, @title, @description, @location,
                @start_utc, @end_utc, @all_day, @timezone, @etag, @updated, @status, @extended_json
            )
            ON CONFLICT(id) DO UPDATE SET
                calendar_id = excluded.calendar_id,
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
                extended_json = excluded.extended_json;";

        string? extendedJson = evt.ExtendedProperties.Count > 0
            ? JsonSerializer.Serialize(evt.ExtendedProperties)
            : null;

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
        cmd.Parameters.AddWithValue("@start_utc", evt.StartUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@end_utc", evt.EndUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@all_day", evt.AllDay ? 1 : 0);
        cmd.Parameters.AddWithValue("@timezone", (object?)evt.TimeZone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@etag", (object?)evt.Etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@updated", evt.Updated.HasValue ? evt.Updated.Value.ToString("o", CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)evt.Status ?? "confirmed");
        cmd.Parameters.AddWithValue("@extended_json", (object?)extendedJson ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
