using System.Data;
using System.Globalization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// SQLite/SQLCipher persistent implementation of <see cref="IAiCommandStore"/> (spec §4.8, §6, FR-QUEUE-1, 2, 8, 9).
/// </summary>
public sealed class SqliteAiCommandStore : IAiCommandStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public SqliteAiCommandStore(ISqliteConnectionFactory connectionFactory, ILogger? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? Log.ForContext<SqliteAiCommandStore>();
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async Task<AiCommand> EnqueueAsync(
        string text,
        DateTimeOffset typedAt,
        string timeZone,
        string inputLang,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputLang);

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        // FR-QUEUE-1: Allocate display number starting at 1000
        using var seqCmd = connection.CreateCommand();
        seqCmd.Transaction = transaction;
        seqCmd.CommandText = "SELECT COALESCE(MAX(display_no), 999) + 1 FROM AiCommand;";
        var seqResult = await seqCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var displayNo = Convert.ToInt32(seqResult, CultureInfo.InvariantCulture);

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = @"
            INSERT INTO AiCommand (
                id, display_no, text, typed_at, timezone, input_lang,
                status, retry_count, next_retry_at, last_error, batch_id,
                created_at, updated_at
            )
            VALUES (
                @id, @display_no, @text, @typed_at, @timezone, @input_lang,
                @status, 0, NULL, NULL, NULL,
                @created_at, @updated_at
            );";

        insertCmd.Parameters.AddWithValue("@id", id);
        insertCmd.Parameters.AddWithValue("@display_no", displayNo);
        insertCmd.Parameters.AddWithValue("@text", text);
        insertCmd.Parameters.AddWithValue("@typed_at", typedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        insertCmd.Parameters.AddWithValue("@timezone", timeZone);
        insertCmd.Parameters.AddWithValue("@input_lang", inputLang);
        insertCmd.Parameters.AddWithValue("@status", AiCommandStatus.Pending);
        insertCmd.Parameters.AddWithValue("@created_at", now.ToString("O", CultureInfo.InvariantCulture));
        insertCmd.Parameters.AddWithValue("@updated_at", now.ToString("O", CultureInfo.InvariantCulture));

        await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();

        _logger.Debug("Enqueued AI command {CommandId} with display #{DisplayNo}", id, displayNo);

        return new AiCommand
        {
            Id = id,
            DisplayNo = displayNo,
            Text = text,
            TypedAt = typedAt,
            TimeZone = timeZone,
            InputLang = inputLang,
            Status = AiCommandStatus.Pending,
            RetryCount = 0,
            NextRetryAt = null,
            LastError = null,
            BatchId = null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <inheritdoc />
    public async Task<AiCommand?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, display_no, text, typed_at, timezone, input_lang,
                   status, retry_count, next_retry_at, last_error, batch_id,
                   created_at, updated_at, proposal_json
            FROM AiCommand
            WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadCommand(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiCommand>> GetPendingQueueAsync(
        DateTimeOffset now,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, display_no, text, typed_at, timezone, input_lang,
                   status, retry_count, next_retry_at, last_error, batch_id,
                   created_at, updated_at, proposal_json
            FROM AiCommand
            WHERE (status = @pendingStatus AND (next_retry_at IS NULL OR next_retry_at <= @now))
               OR (status = @errorStatus AND retry_count < 5 AND next_retry_at IS NOT NULL AND next_retry_at <= @now)
            ORDER BY created_at ASC
            LIMIT @limit;";

        command.Parameters.AddWithValue("@pendingStatus", AiCommandStatus.Pending);
        command.Parameters.AddWithValue("@errorStatus", AiCommandStatus.Error);
        command.Parameters.AddWithValue("@now", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@limit", limit);

        var list = new List<AiCommand>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadCommand(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiCommand>> GetAllAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, display_no, text, typed_at, timezone, input_lang,
                   status, retry_count, next_retry_at, last_error, batch_id,
                   created_at, updated_at, proposal_json
            FROM AiCommand
            ORDER BY created_at DESC
            LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", limit);

        var list = new List<AiCommand>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadCommand(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        string id,
        string status,
        string? lastError = null,
        int? retryCount = null,
        DateTimeOffset? nextRetryAt = null,
        string? batchId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var now = DateTimeOffset.UtcNow;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();

        var query = @"
            UPDATE AiCommand
            SET status = @status,
                updated_at = @updated_at";

        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@updated_at", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@id", id);

        if (lastError is not null)
        {
            query += ", last_error = @last_error";
            command.Parameters.AddWithValue("@last_error", lastError);
        }

        if (retryCount.HasValue)
        {
            query += ", retry_count = @retry_count";
            command.Parameters.AddWithValue("@retry_count", retryCount.Value);
        }

        if (nextRetryAt.HasValue)
        {
            query += ", next_retry_at = @next_retry_at";
            command.Parameters.AddWithValue("@next_retry_at", nextRetryAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            // Always clear a retry time that is not being set. A command that failed permanently (invalid key, daily
            // quota) used to keep the retry time of its earlier transient failure, so GetPendingQueueAsync picked it up
            // again immediately and the worker called Gemini for it in an endless loop (P4-R9).
            query += ", next_retry_at = NULL";
        }

        if (batchId is not null)
        {
            query += ", batch_id = @batch_id";
            command.Parameters.AddWithValue("@batch_id", batchId);
        }

        query += " WHERE id = @id;";
        command.CommandText = query;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.Debug("Updated status of AI command {CommandId} to {Status}", id, status);
    }

    /// <inheritdoc />
    public async Task SetProposalAsync(string id, AiCommandProposal? proposal, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE AiCommand
            SET proposal_json = @proposal_json,
                updated_at = @updated_at
            WHERE id = @id;";
        command.Parameters.AddWithValue("@proposal_json", proposal is null ? DBNull.Value : proposal.ToJson());
        command.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> RecoverProcessingCommandsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE AiCommand
            SET status = @pendingStatus,
                updated_at = @updated_at
            WHERE status = @processingStatus;";

        command.Parameters.AddWithValue("@pendingStatus", AiCommandStatus.Pending);
        command.Parameters.AddWithValue("@processingStatus", AiCommandStatus.Processing);
        command.Parameters.AddWithValue("@updated_at", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows > 0)
        {
            _logger.Information("Crash recovery: reset {Count} commands from processing to pending", rows);
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<int> CleanupOldCommandsAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM AiCommand
            WHERE status IN (@completedStatus, @cancelledStatus)
              AND updated_at < @cutoff;";

        command.Parameters.AddWithValue("@completedStatus", AiCommandStatus.Completed);
        command.Parameters.AddWithValue("@cancelledStatus", AiCommandStatus.Cancelled);
        command.Parameters.AddWithValue("@cutoff", cutoff.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows > 0)
        {
            _logger.Information("Cleaned up {Count} finished commands older than {Cutoff}", rows, cutoff);
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*) FROM AiCommand
            WHERE status NOT IN (@completedStatus, @cancelledStatus);";

        command.Parameters.AddWithValue("@completedStatus", AiCommandStatus.Completed);
        command.Parameters.AddWithValue("@cancelledStatus", AiCommandStatus.Cancelled);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await UpdateStatusAsync(id, AiCommandStatus.Cancelled, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RetryNowAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var now = DateTimeOffset.UtcNow;
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE AiCommand
            SET status = @pendingStatus,
                next_retry_at = NULL,
                updated_at = @updated_at
            WHERE id = @id;";

        command.Parameters.AddWithValue("@pendingStatus", AiCommandStatus.Pending);
        command.Parameters.AddWithValue("@updated_at", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AiCommand ReadCommand(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var displayNo = reader.GetInt32(1);
        var text = reader.GetString(2);
        var typedAtStr = reader.GetString(3);
        var timezone = reader.GetString(4);
        var inputLang = reader.GetString(5);
        var status = reader.GetString(6);
        var retryCount = reader.GetInt32(7);
        var nextRetryAtStr = reader.IsDBNull(8) ? null : reader.GetString(8);
        var lastError = reader.IsDBNull(9) ? null : reader.GetString(9);
        var batchId = reader.IsDBNull(10) ? null : reader.GetString(10);
        var createdAtStr = reader.GetString(11);
        var updatedAtStr = reader.GetString(12);
        var proposal = AiCommandProposal.FromJson(reader.IsDBNull(13) ? null : reader.GetString(13));

        var typedAt = DateTimeOffset.TryParse(typedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ta)
            ? AiCommandTime.InZone(ta, timezone) // stored as UTC: restore the user's local offset (FR-QUEUE-4)
            : DateTimeOffset.UtcNow;

        DateTimeOffset? nextRetryAt = null;
        if (nextRetryAtStr is not null && DateTimeOffset.TryParse(nextRetryAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nr))
        {
            nextRetryAt = nr;
        }

        var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ca)
            ? ca
            : DateTimeOffset.UtcNow;

        var updatedAt = DateTimeOffset.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ua)
            ? ua
            : DateTimeOffset.UtcNow;

        return new AiCommand
        {
            Id = id,
            DisplayNo = displayNo,
            Text = text,
            TypedAt = typedAt,
            TimeZone = timezone,
            InputLang = inputLang,
            Status = status,
            RetryCount = retryCount,
            NextRetryAt = nextRetryAt,
            LastError = lastError,
            BatchId = batchId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Proposal = proposal
        };
    }
}
