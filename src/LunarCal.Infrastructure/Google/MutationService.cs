using System.Globalization;
using System.Net;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Service managing optimistic UI writes, offline queuing and synchronization dispatching (FR-SYNC-4, NFR-REL).
/// </summary>
public sealed class MutationService : IMutationService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IPendingMutationStore _mutationStore;
    private readonly ICalendarGateway _gateway;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly IConflictNotifier _conflictNotifier;
    private readonly IEventStore? _eventStore;
    private ISyncScheduler? _syncScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Gets or sets the sync scheduler used to trigger post-mutation sync passes.
    /// </summary>
    public ISyncScheduler? SyncScheduler
    {
        get => _syncScheduler;
        set => _syncScheduler = value;
    }

    public MutationService(
        ISqliteConnectionFactory connectionFactory,
        IPendingMutationStore mutationStore,
        ICalendarGateway gateway,
        IGoogleOAuthClient oauthClient,
        IConflictNotifier conflictNotifier,
        ISyncScheduler? syncScheduler = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        IEventStore? eventStore = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _mutationStore = mutationStore ?? throw new ArgumentNullException(nameof(mutationStore));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _conflictNotifier = conflictNotifier ?? throw new ArgumentNullException(nameof(conflictNotifier));
        _eventStore = eventStore;
        _syncScheduler = syncScheduler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? Log.ForContext<MutationService>();
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async Task<CalendarEvent> CreateEventOptimisticAsync(
        string accountId,
        string calendarId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        // The local id is also the Google event id (sent in the insert body). Google accepts client ids made of
        // base32hex characters (a-v, 0-9), 5-1024 long; a "N"-format GUID qualifies. Using one id everywhere means
        // the next sync updates this row instead of adding a duplicate, and a retried insert cannot create two
        // events (409 is treated as success) (FR-SYNC-4, SEC-NET-2).
        var eventId = IsValidGoogleEventId(calendarEvent.Id) ? calendarEvent.Id : NewGoogleEventId();
        var localEvent = calendarEvent with { Id = eventId, GoogleId = eventId, CalendarId = calendarId };

        var payload = new EventMutationPayload
        {
            AccountId = accountId,
            CalendarId = calendarId,
            EventId = eventId,
            Event = localEvent,
            OriginalEtag = null,
            ClientRequestId = Guid.NewGuid().ToString("N")
        };

        var mutation = new PendingMutation
        {
            Id = Guid.NewGuid().ToString("D"),
            Kind = MutationKind.CreateEvent,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = _timeProvider.GetUtcNow(),
            Attempts = 0,
            LastError = null
        };

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await UpsertEventAsync(connection, transaction, localEvent, cancellationToken).ConfigureAwait(false);
            await EnqueueMutationInTransactionAsync(connection, transaction, mutation, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (_eventStore is not null)
            {
                await _eventStore.UpsertEventAsync(localEvent, cancellationToken).ConfigureAwait(false);
            }
            _logger.Information("Optimistically created event {EventId} in calendar {CalendarId} (FR-SYNC-4)", eventId, calendarId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Failed to persist optimistic create for event {EventId}", eventId);
            throw;
        }

        // Trigger post-mutation sync pass asynchronously (FR-SYNC-3)
        if (_syncScheduler is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncScheduler.TriggerPostMutationSyncAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to trigger post-mutation sync after creating event {EventId}", eventId);
                }
            }, CancellationToken.None);
        }

        return localEvent;
    }

    /// <inheritdoc />
    public async Task<CalendarEvent> UpdateEventOptimisticAsync(
        string accountId,
        string calendarId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentNullException.ThrowIfNull(calendarEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarEvent.Id);

        var payload = new EventMutationPayload
        {
            AccountId = accountId,
            CalendarId = calendarId,
            EventId = calendarEvent.Id,
            Event = calendarEvent,
            OriginalEtag = calendarEvent.Etag,
            ClientRequestId = Guid.NewGuid().ToString("N")
        };

        var mutation = new PendingMutation
        {
            Id = Guid.NewGuid().ToString("D"),
            Kind = MutationKind.UpdateEvent,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = _timeProvider.GetUtcNow(),
            Attempts = 0,
            LastError = null
        };

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await UpsertEventAsync(connection, transaction, calendarEvent, cancellationToken).ConfigureAwait(false);
            await EnqueueMutationInTransactionAsync(connection, transaction, mutation, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (_eventStore is not null)
            {
                await _eventStore.UpsertEventAsync(calendarEvent, cancellationToken).ConfigureAwait(false);
            }
            _logger.Information("Optimistically updated event {EventId} in calendar {CalendarId} (FR-SYNC-4)", calendarEvent.Id, calendarId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Failed to persist optimistic update for event {EventId}", calendarEvent.Id);
            throw;
        }

        if (_syncScheduler is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncScheduler.TriggerPostMutationSyncAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to trigger post-mutation sync after updating event {EventId}", calendarEvent.Id);
                }
            }, CancellationToken.None);
        }

        return calendarEvent;
    }

    /// <inheritdoc />
    public async Task DeleteEventOptimisticAsync(
        string accountId,
        string calendarId,
        string eventId,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        var payload = new EventMutationPayload
        {
            AccountId = accountId,
            CalendarId = calendarId,
            EventId = eventId,
            Event = null,
            OriginalEtag = etag,
            ClientRequestId = Guid.NewGuid().ToString("N")
        };

        var mutation = new PendingMutation
        {
            Id = Guid.NewGuid().ToString("D"),
            Kind = MutationKind.DeleteEvent,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = _timeProvider.GetUtcNow(),
            Attempts = 0,
            LastError = null
        };

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            await DeleteEventFromDbAsync(connection, transaction, eventId, cancellationToken).ConfigureAwait(false);
            await EnqueueMutationInTransactionAsync(connection, transaction, mutation, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (_eventStore is not null)
            {
                await _eventStore.DeleteEventAsync(eventId, cancellationToken).ConfigureAwait(false);
            }
            _logger.Information("Optimistically deleted event {EventId} from calendar {CalendarId} (FR-SYNC-4)", eventId, calendarId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Failed to persist optimistic delete for event {EventId}", eventId);
            throw;
        }

        if (_syncScheduler is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncScheduler.TriggerPostMutationSyncAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to trigger post-mutation sync after deleting event {EventId}", eventId);
                }
            }, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task<MutationProcessingResult> ProcessPendingMutationsAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var mutations = await _mutationStore.GetPendingAsync(limit: 100, cancellationToken).ConfigureAwait(false);
        if (mutations.Count == 0)
        {
            return MutationProcessingResult.Empty;
        }

        _logger.Information("Processing {Count} pending mutations from offline queue (FR-SYNC-4, NFR-REL)", mutations.Count);

        int processed = 0;
        int succeeded = 0;
        int failed = 0;
        int conflicts = 0;

        foreach (var mutation in mutations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventMutationPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<EventMutationPayload>(mutation.PayloadJson);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize payload for mutation {MutationId}. Deleting malformed mutation.", mutation.Id);
                await _mutationStore.DeleteAsync(mutation.Id, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (payload is null)
            {
                await _mutationStore.DeleteAsync(mutation.Id, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(accountId) && !string.Equals(payload.AccountId, accountId, StringComparison.Ordinal))
            {
                continue;
            }

            processed++;

            // Acquire valid access token (refreshes if needed per FR-AUTH-3)
            var tokenResult = await _oauthClient.TryRefreshAccessTokenAsync(payload.AccountId, cancellationToken).ConfigureAwait(false);
            var accessToken = tokenResult.Tokens?.AccessToken;
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(accessToken))
            {
                var msg = tokenResult.GuidanceMessage ?? "Cannot acquire access token";
                _logger.Warning("Halting mutation dispatch for account {AccountId}: {Reason}", payload.AccountId, msg);
                await _mutationStore.MarkAttemptFailedAsync(mutation.Id, msg, cancellationToken).ConfigureAwait(false);
                failed++;
                return new MutationProcessingResult(processed, succeeded, failed, conflicts, IsOffline: true, Message: msg);
            }

            try
            {
                switch (mutation.Kind)
                {
                    case MutationKind.CreateEvent:
                        await DispatchCreateEventAsync(accessToken, payload, cancellationToken).ConfigureAwait(false);
                        break;

                    case MutationKind.UpdateEvent:
                        var updateConflict = await DispatchUpdateEventAsync(accessToken, payload, cancellationToken).ConfigureAwait(false);
                        if (updateConflict)
                        {
                            conflicts++;
                        }
                        break;

                    case MutationKind.DeleteEvent:
                        var deleteConflict = await DispatchDeleteEventAsync(accessToken, payload, cancellationToken).ConfigureAwait(false);
                        if (deleteConflict)
                        {
                            conflicts++;
                        }
                        break;
                }

                // Mutation successfully applied or conflict resolved per server-wins
                await _mutationStore.DeleteAsync(mutation.Id, cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (GooglePreconditionFailedException ex)
            {
                // This shouldn't normally escape DispatchUpdateEvent/DispatchDeleteEvent, but if it does:
                conflicts++;
                await HandleConflictAsync(accessToken, payload, ex.Message, cancellationToken).ConfigureAwait(false);
                await _mutationStore.DeleteAsync(mutation.Id, cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (HttpRequestException ex) when (IsNetworkOrOfflineError(ex))
            {
                _logger.Warning("Offline or network error dispatching mutation {MutationId}: {Error}", mutation.Id, ex.Message);
                await _mutationStore.MarkAttemptFailedAsync(mutation.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                failed++;
                // Halt on network failure to preserve FIFO ordering for subsequent mutations
                return new MutationProcessingResult(processed, succeeded, failed, conflicts, IsOffline: true, Message: ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error dispatching mutation {MutationId}", mutation.Id);
                await _mutationStore.MarkAttemptFailedAsync(mutation.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                failed++;
                // Continue to next mutation or halt depending on severity
            }
        }

        return new MutationProcessingResult(processed, succeeded, failed, conflicts, IsOffline: false, Message: null);
    }

    private async Task DispatchCreateEventAsync(
        string accessToken,
        EventMutationPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Event is null)
        {
            throw new InvalidOperationException("Payload.Event cannot be null for CreateEvent");
        }

        var remoteEvent = await _gateway.CreateEventAsync(
            accessToken,
            payload.CalendarId,
            payload.Event,
            payload.ClientRequestId,
            cancellationToken).ConfigureAwait(false);

        // Update local database: link local event with Google ID and remote ETag
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Event
            SET google_id = @google_id, etag = @etag, updated = @updated
            WHERE id = @local_id;";

        command.Parameters.AddWithValue("@google_id", (object?)remoteEvent.GoogleId ?? remoteEvent.Id);
        command.Parameters.AddWithValue("@etag", (object?)remoteEvent.Etag ?? DBNull.Value);
        command.Parameters.AddWithValue("@updated", remoteEvent.Updated.HasValue
            ? remoteEvent.Updated.Value.ToString("o", CultureInfo.InvariantCulture)
            : DBNull.Value);
        command.Parameters.AddWithValue("@local_id", payload.EventId);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // The cache holds expanded instances (singleEvents=true). A recurring master would show up next to its own
        // first instance after the post-mutation sync, so drop the optimistic master row now.
        if (payload.Event.Recurrence is { Count: > 0 })
        {
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM Event WHERE id = @local_id;";
            cleanup.Parameters.AddWithValue("@local_id", payload.EventId);
            await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.Information("Successfully created remote event {GoogleId} for local event {LocalId}", remoteEvent.GoogleId, payload.EventId);
    }

    private async Task<bool> DispatchUpdateEventAsync(
        string accessToken,
        EventMutationPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Event is null)
        {
            throw new InvalidOperationException("Payload.Event cannot be null for UpdateEvent");
        }

        try
        {
            var updatedRemote = await _gateway.UpdateEventAsync(
                accessToken,
                payload.CalendarId,
                payload.Event,
                payload.OriginalEtag,
                payload.ClientRequestId,
                cancellationToken).ConfigureAwait(false);

            // Update local SQLite cache with latest server ETag and updated timestamp
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Event
                SET etag = @etag, updated = @updated
                WHERE id = @id OR google_id = @id;";

            command.Parameters.AddWithValue("@etag", (object?)updatedRemote.Etag ?? DBNull.Value);
            command.Parameters.AddWithValue("@updated", updatedRemote.Updated.HasValue
                ? updatedRemote.Updated.Value.ToString("o", CultureInfo.InvariantCulture)
                : DBNull.Value);
            command.Parameters.AddWithValue("@id", payload.EventId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.Information("Successfully updated remote event {EventId}", payload.EventId);
            return false;
        }
        catch (GooglePreconditionFailedException ex)
        {
            // HTTP 412 Conflict: Server wins per spec FR-SYNC-4
            _logger.Warning("Update conflict on event {EventId}. Applying server-wins policy (FR-SYNC-4)", payload.EventId);
            await HandleConflictAsync(accessToken, payload, ex.Message, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private async Task<bool> DispatchDeleteEventAsync(
        string accessToken,
        EventMutationPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.DeleteEventAsync(
                accessToken,
                payload.CalendarId,
                payload.EventId,
                payload.OriginalEtag,
                payload.ClientRequestId,
                cancellationToken).ConfigureAwait(false);

            _logger.Information("Successfully deleted remote event {EventId}", payload.EventId);
            return false;
        }
        catch (GooglePreconditionFailedException ex)
        {
            // HTTP 412 Conflict: Server wins per spec FR-SYNC-4
            _logger.Warning("Delete conflict on event {EventId}. Applying server-wins policy (FR-SYNC-4)", payload.EventId);
            await HandleConflictAsync(accessToken, payload, ex.Message, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private async Task HandleConflictAsync(
        string accessToken,
        EventMutationPayload payload,
        string reason,
        CancellationToken cancellationToken)
    {
        // 1. Fetch server's authoritative version
        CalendarEvent? serverEvent = null;
        try
        {
            serverEvent = await _gateway.GetEventAsync(accessToken, payload.CalendarId, payload.EventId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to fetch server event {EventId} during conflict resolution", payload.EventId);
        }

        // 2. Apply server version to local SQLite storage (server wins)
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        try
        {
            if (serverEvent is not null && !string.Equals(serverEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await UpsertEventAsync(connection, transaction, serverEvent, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DeleteEventFromDbAsync(connection, transaction, payload.EventId, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (_eventStore is not null)
            {
                if (serverEvent is not null && !string.Equals(serverEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    await _eventStore.UpsertEventAsync(serverEvent, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _eventStore.DeleteEventAsync(payload.EventId, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Failed to apply server-wins conflict update in SQLite for event {EventId}", payload.EventId);
        }

        // 3. Notify user / UI per FR-SYNC-4
        _conflictNotifier.NotifyConflict(new EventConflictNotice(
            payload.AccountId,
            payload.CalendarId,
            payload.EventId,
            serverEvent?.Title ?? payload.Event?.Title,
            reason,
            _timeProvider.GetUtcNow()));
    }

    private static Task UpsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CalendarEvent evt,
        CancellationToken cancellationToken) =>
        EventRows.UpsertEventAsync(connection, transaction, evt, cancellationToken);

    private static async Task DeleteEventFromDbAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM Event WHERE id = @id OR google_id = @id;";
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", eventId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnqueueMutationInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingMutation mutation,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO PendingMutation (id, kind, payload_json, created_at, attempts, last_error)
            VALUES (@id, @kind, @payload_json, @created_at, @attempts, @last_error);";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", mutation.Id);
        cmd.Parameters.AddWithValue("@kind", mutation.Kind.ToString());
        cmd.Parameters.AddWithValue("@payload_json", mutation.PayloadJson);
        cmd.Parameters.AddWithValue("@created_at", mutation.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@attempts", mutation.Attempts);
        cmd.Parameters.AddWithValue("@last_error", (object?)mutation.LastError ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates an event id Google accepts as a client-supplied id (lowercase hex, 32 characters).</summary>
    public static string NewGoogleEventId() => Guid.NewGuid().ToString("N");

    /// <summary>True when <paramref name="id"/> is a valid client-supplied Google event id (base32hex, 5-1024 chars).</summary>
    public static bool IsValidGoogleEventId(string? id) =>
        id is { Length: >= 5 and <= 1024 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'v'));

    private static bool IsNetworkOrOfflineError(HttpRequestException ex)
    {
        if (ex.StatusCode is null)
        {
            return true;
        }

        return ex.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway;
    }
}
