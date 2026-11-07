using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Storage;
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
    private readonly AccountDataPurger _purger;
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
        _purger = new AccountDataPurger(connectionFactory);
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

        // FR-AUTH-4: delete the account and its cache, plus the queues that cannot be told apart per account (ADR-0029)
        await _purger.PurgeAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        _logger.Information("Successfully purged database cache for account {AccountId}.", accountId);

        return GoogleSignOutResult.Succeeded(tokenRevokedOnServer: tokenRevoked);
    }
}
