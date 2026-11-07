using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Google account sign-out service implementing FR-AUTH-4:
/// Revokes Google token, deletes refresh token from DPAPI secret store, and purges all account data and events from SQLite cache.
/// Requires explicit user confirmation before executing.
/// </summary>
public sealed class GoogleSignOutService : IGoogleSignOutService
{
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly ISecretStore _secretStore;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public GoogleSignOutService(
        IGoogleOAuthClient oauthClient,
        ISecretStore secretStore,
        ISqliteConnectionFactory connectionFactory,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(oauthClient);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _oauthClient = oauthClient;
        _secretStore = secretStore;
        _connectionFactory = connectionFactory;
        _logger = logger ?? Log.ForContext<GoogleSignOutService>();
    }

    /// <inheritdoc />
    public async Task<GoogleSignOutResult> SignOutAsync(string accountId, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        // FR-AUTH-4: "có xác nhận" - must be confirmed by user
        if (!confirmed)
        {
            _logger.Information("Sign-out for account {AccountId} aborted because confirmation was not provided.", accountId);
            return GoogleSignOutResult.Unconfirmed();
        }

        _logger.Information("Starting sign-out process for account {AccountId} (FR-AUTH-4)...", accountId);

        var secretKey = KnownSecretKeys.GoogleRefreshToken(accountId);
        var storedRefreshToken = await _secretStore.GetSecretAsync(secretKey, cancellationToken).ConfigureAwait(false);

        var tokenRevoked = false;
        if (!string.IsNullOrWhiteSpace(storedRefreshToken))
        {
            try
            {
                tokenRevoked = await _oauthClient.RevokeTokenAsync(storedRefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Token revocation request failed for account {AccountId}.", accountId);
            }
        }
        else
        {
            _logger.Information("No refresh token found for account {AccountId} to revoke.", accountId);
            tokenRevoked = true;
        }

        // SEC-OAUTH-3, FR-AUTH-4: Delete stored refresh token from DPAPI secret store
        await _secretStore.DeleteSecretAsync(secretKey, cancellationToken).ConfigureAwait(false);
        _logger.Information("Deleted DPAPI refresh token for account {AccountId}.", accountId);

        // FR-AUTH-4: Delete account and cache DB of that account
        await PurgeAccountDataFromDatabaseAsync(accountId, cancellationToken).ConfigureAwait(false);
        _logger.Information("Successfully purged database cache for account {AccountId}.", accountId);

        return GoogleSignOutResult.Succeeded(tokenRevokedOnServer: tokenRevoked);
    }

    private async Task PurgeAccountDataFromDatabaseAsync(string accountId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // 1. Delete ReminderState entries for events in account calendars
        const string deleteRemindersSql = """
            DELETE FROM ReminderState
            WHERE event_id IN (
                SELECT e.id FROM Event e
                INNER JOIN CalendarInfo c ON e.calendar_id = c.id
                WHERE c.account_id = $accountId
            );
            """;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = deleteRemindersSql;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // 2. Delete LunarRules associated with account calendars
        const string deleteLunarRulesSql = """
            DELETE FROM LunarRule
            WHERE calendar_id IN (
                SELECT id FROM CalendarInfo WHERE account_id = $accountId
            );
            """;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = deleteLunarRulesSql;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // 3. Delete Events for account calendars
        const string deleteEventsSql = """
            DELETE FROM Event
            WHERE calendar_id IN (
                SELECT id FROM CalendarInfo WHERE account_id = $accountId
            );
            """;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = deleteEventsSql;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // 4. Delete CalendarInfo for the account
        const string deleteCalendarsSql = """
            DELETE FROM CalendarInfo
            WHERE account_id = $accountId;
            """;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = deleteCalendarsSql;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // 5. Delete the Account record
        const string deleteAccountSql = """
            DELETE FROM Account
            WHERE id = $accountId;
            """;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = deleteAccountSql;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
