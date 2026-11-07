using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Google;

/// <summary>What would be lost together with the cached Google data (ADR-0029).</summary>
/// <param name="PendingMutations">Offline calendar changes not yet sent to Google.</param>
/// <param name="PendingAiCommands">AI commands that are not finished.</param>
/// <param name="LunarRules">Lunar recurrence rules stored only on this machine.</param>
public sealed record AccountLocalDataCounts(int PendingMutations, int PendingAiCommands, int LunarRules);

/// <summary>
/// Deletes the local data that belongs to the Google account: the cache (calendars, events, reminders), the lunar
/// rules, and the queues of work addressed to that account (FR-AUTH-4, FR-AUTH-5, SEC-DATA-3, ADR-0029).
/// <c>PendingMutation</c> and <c>AiCommand</c> have no account column. The app holds one account at a time, so they are
/// always deleted together with the account; otherwise changes made for the old account would later be replayed against
/// the new one.
/// </summary>
public sealed class AccountDataPurger
{
    /// <summary>
    /// <c>AiCommand.status</c> values that mean "finished" (FR-QUEUE-2: Hoàn tất, Đã huỷ). Every other status counts as
    /// pending work. The AI queue (P4-08) must store these exact codes for its terminal states.
    /// </summary>
    public static readonly IReadOnlyList<string> TerminalAiCommandStatuses = ["completed", "cancelled"];

    private readonly ISqliteConnectionFactory _connectionFactory;

    /// <summary>Creates the purger over the encrypted local database.</summary>
    public AccountDataPurger(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>Counts the work and rules that a purge would destroy.</summary>
    public async Task<AccountLocalDataCounts> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var mutations = await ScalarAsync(connection, "SELECT COUNT(*) FROM PendingMutation;", null, cancellationToken).ConfigureAwait(false);

        var placeholders = string.Join(", ", TerminalAiCommandStatuses.Select((_, i) => $"$s{i}"));
        var statusParameters = TerminalAiCommandStatuses
            .Select((status, i) => new SqliteParameter($"$s{i}", status))
            .ToArray();
        var commands = await ScalarAsync(
            connection, $"SELECT COUNT(*) FROM AiCommand WHERE status NOT IN ({placeholders});", statusParameters, cancellationToken).ConfigureAwait(false);

        var rules = await ScalarAsync(connection, "SELECT COUNT(*) FROM LunarRule;", null, cancellationToken).ConfigureAwait(false);
        return new AccountLocalDataCounts(mutations, commands, rules);
    }

    /// <summary>Deletes the data of one account (sign-out, FR-AUTH-4) and the account-less queues.</summary>
    public async Task PurgeAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var scoped = new[]
        {
            """
            DELETE FROM ReminderState
            WHERE event_id IN (
                SELECT e.id FROM Event e
                INNER JOIN CalendarInfo c ON e.calendar_id = c.id
                WHERE c.account_id = $accountId
            );
            """,
            "DELETE FROM LunarRule WHERE calendar_id IN (SELECT id FROM CalendarInfo WHERE account_id = $accountId);",
            "DELETE FROM Event WHERE calendar_id IN (SELECT id FROM CalendarInfo WHERE account_id = $accountId);",
            "DELETE FROM CalendarInfo WHERE account_id = $accountId;",
            "DELETE FROM Account WHERE id = $accountId;",
        };

        foreach (var sql in scoped)
        {
            await ExecuteAsync(connection, transaction, sql, new SqliteParameter("$accountId", accountId), cancellationToken).ConfigureAwait(false);
        }

        await DeleteQueuesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every account and everything addressed to it (switching to a different account, ADR-0029). Rows of any
    /// older, second account left over from before ADR-0029 go too.
    /// </summary>
    public async Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var table in new[] { "ReminderState", "LunarRule", "Event", "CalendarInfo", "Account" })
        {
            await ExecuteAsync(connection, transaction, $"DELETE FROM {table};", null, cancellationToken).ConfigureAwait(false);
        }

        await DeleteQueuesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteQueuesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM PendingMutation;", null, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, "DELETE FROM AiCommand;", null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, SqliteParameter? parameter, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (parameter is not null)
        {
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ScalarAsync(
        SqliteConnection connection, string sql, SqliteParameter[]? parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameters is not null)
        {
            command.Parameters.AddRange(parameters);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
