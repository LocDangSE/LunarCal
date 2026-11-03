using System.Data.Common;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// SQLite-backed store for user account records (spec §6 Account table, FR-AUTH-1, FR-AUTH-4).
/// </summary>
public sealed class SqliteAccountStore : IAccountStore
{
    private readonly ISqliteConnectionFactory? _connectionFactory;
    private readonly ILogger _logger;
    private readonly List<AccountInfo> _fallbackAccounts = new();
    private readonly object _lock = new();

    public SqliteAccountStore(ISqliteConnectionFactory? connectionFactory = null, ILogger? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger ?? Log.ForContext<SqliteAccountStore>();
    }

    /// <inheritdoc />
    public async Task<AccountInfo?> GetPrimaryAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionFactory is null)
        {
            lock (_lock)
            {
                return _fallbackAccounts.FirstOrDefault();
            }
        }

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, email, display_name FROM Account LIMIT 1;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new AccountInfo
                {
                    Id = reader.GetString(0),
                    Email = reader.GetString(1),
                    DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read primary account from SQLite cache.");
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountInfo>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionFactory is null)
        {
            lock (_lock)
            {
                return _fallbackAccounts.ToList();
            }
        }

        var accounts = new List<AccountInfo>();
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, email, display_name FROM Account;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                accounts.Add(new AccountInfo
                {
                    Id = reader.GetString(0),
                    Email = reader.GetString(1),
                    DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read accounts from SQLite cache.");
        }

        return accounts;
    }

    /// <inheritdoc />
    public async Task SaveAccountAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        lock (_lock)
        {
            _fallbackAccounts.RemoveAll(a => a.Id == account.Id);
            _fallbackAccounts.Add(account);
        }

        if (_connectionFactory is null)
        {
            return;
        }

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Account (id, email, display_name)
                VALUES (@id, @email, @display_name)
                ON CONFLICT(id) DO UPDATE SET
                    email = excluded.email,
                    display_name = excluded.display_name;";

            command.Parameters.Add(new SqliteParameter("@id", account.Id));
            command.Parameters.Add(new SqliteParameter("@email", account.Email));
            command.Parameters.Add(new SqliteParameter("@display_name", (object?)account.DisplayName ?? DBNull.Value));

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.Information("Saved account {Email} (ID: {AccountId}) to SQLite storage.", account.Email, account.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save account {AccountId} to SQLite.", account.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_lock)
        {
            _fallbackAccounts.RemoveAll(a => a.Id == accountId);
        }

        if (_connectionFactory is null)
        {
            return;
        }

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Account WHERE id = @id;";
            command.Parameters.Add(new SqliteParameter("@id", accountId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.Information("Deleted account {AccountId} from SQLite storage.", accountId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete account {AccountId} from SQLite.", accountId);
            throw;
        }
    }
}
