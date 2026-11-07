using System.Data;
using System.Globalization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// SQLite implementation of <see cref="IPendingMutationStore"/> (spec §6, FR-SYNC-4, NFR-REL).
/// </summary>
public sealed class SqlitePendingMutationStore : IPendingMutationStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public SqlitePendingMutationStore(ISqliteConnectionFactory connectionFactory, ILogger? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? Log.ForContext<SqlitePendingMutationStore>();
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(PendingMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PendingMutation (id, kind, payload_json, created_at, attempts, last_error)
            VALUES (@id, @kind, @payload_json, @created_at, @attempts, @last_error);";

        command.Parameters.AddWithValue("@id", mutation.Id);
        command.Parameters.AddWithValue("@kind", mutation.Kind.ToString());
        command.Parameters.AddWithValue("@payload_json", mutation.PayloadJson);
        command.Parameters.AddWithValue("@created_at", mutation.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@attempts", mutation.Attempts);
        command.Parameters.AddWithValue("@last_error", (object?)mutation.LastError ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.Debug("Enqueued mutation {MutationId} of kind {Kind}", mutation.Id, mutation.Kind);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingMutation>> GetPendingAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, kind, payload_json, created_at, attempts, last_error
            FROM PendingMutation
            ORDER BY created_at ASC
            LIMIT @limit;";

        command.Parameters.AddWithValue("@limit", limit);

        var list = new List<PendingMutation>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadMutation(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<PendingMutation?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, kind, payload_json, created_at, attempts, last_error
            FROM PendingMutation
            WHERE id = @id;";

        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadMutation(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task MarkAttemptFailedAsync(string id, string errorMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE PendingMutation
            SET attempts = attempts + 1, last_error = @last_error
            WHERE id = @id;";

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@last_error", errorMessage ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.Warning("Marked attempt failure on mutation {MutationId}: {Error}", id, errorMessage);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingMutation WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.Debug("Deleted mutation {MutationId}", id);
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PendingMutation;";

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static PendingMutation ReadMutation(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var kindStr = reader.GetString(1);
        var payloadJson = reader.GetString(2);
        var createdAtStr = reader.GetString(3);
        var attempts = reader.GetInt32(4);
        var lastError = reader.IsDBNull(5) ? null : reader.GetString(5);

        var kind = Enum.TryParse<MutationKind>(kindStr, ignoreCase: true, out var k)
            ? k
            : MutationKind.CreateEvent;

        var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTimeOffset.UtcNow;

        return new PendingMutation
        {
            Id = id,
            Kind = kind,
            PayloadJson = payloadJson,
            CreatedAt = createdAt,
            Attempts = attempts,
            LastError = lastError
        };
    }
}
