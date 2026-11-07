using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Serilog;

namespace LunarCal.Infrastructure.Google;

/// <summary>
/// One Google account at a time (FR-AUTH-5, ADR-0029). Same or first account: save and keep everything. Different
/// account: after the user confirms, revoke and delete the old tokens, delete all local data of the old account and
/// save the new one; when the user declines, discard the tokens the OAuth flow just stored for the new account.
/// </summary>
public sealed class AccountSessionService : IAccountSessionService
{
    private readonly IAccountStore _accounts;
    private readonly ISecretStore _secrets;
    private readonly IGoogleOAuthClient _oauthClient;
    private readonly AccountDataPurger _purger;
    private readonly ILogger _logger;

    /// <summary>Creates the service.</summary>
    public AccountSessionService(
        IAccountStore accounts,
        ISecretStore secrets,
        IGoogleOAuthClient oauthClient,
        AccountDataPurger purger,
        ILogger? logger = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _oauthClient = oauthClient ?? throw new ArgumentNullException(nameof(oauthClient));
        _purger = purger ?? throw new ArgumentNullException(nameof(purger));
        _logger = logger ?? Log.ForContext<AccountSessionService>();
    }

    /// <inheritdoc />
    public async Task<AccountSignInOutcome> ApplySignInAsync(
        GoogleUserInfo signedInUser,
        Func<AccountSignInAssessment, CancellationToken, Task<bool>> confirmSwitch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signedInUser);
        ArgumentNullException.ThrowIfNull(confirmSwitch);

        var incoming = new AccountInfo { Id = signedInUser.Id, Email = signedInUser.Email, DisplayName = signedInUser.DisplayName };
        var stored = await _accounts.GetAllAccountsAsync(cancellationToken).ConfigureAwait(false);

        // Compare by Google user id, never by e-mail (an address can change or be re-used).
        var kind = stored.Count == 0
            ? AccountSignInKind.FirstAccount
            : stored.All(a => string.Equals(a.Id, incoming.Id, StringComparison.Ordinal))
                ? AccountSignInKind.SameAccount
                : AccountSignInKind.DifferentAccount;

        if (kind != AccountSignInKind.DifferentAccount)
        {
            await _accounts.SaveAccountAsync(incoming, cancellationToken).ConfigureAwait(false);
            _logger.Information("Signed in ({Kind}) as account {AccountId}; local data kept (ADR-0029).", kind, incoming.Id);
            return AccountSignInOutcome.Saved;
        }

        var counts = await _purger.CountAsync(cancellationToken).ConfigureAwait(false);
        var assessment = new AccountSignInAssessment
        {
            Kind = kind,
            CurrentAccount = stored[0],
            IncomingAccount = incoming,
            PendingChanges = counts.PendingMutations,
            PendingAiCommands = counts.PendingAiCommands,
            LocalLunarRules = counts.LunarRules,
        };

        var confirmed = await confirmSwitch(assessment, cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            await DiscardTokensAsync(incoming.Id).ConfigureAwait(false);
            _logger.Information("Switch to account {AccountId} declined; new tokens discarded, old data untouched.", incoming.Id);
            return AccountSignInOutcome.Cancelled;
        }

        // Delete the data first (one transaction). If it fails nothing else was touched and the exception reaches the caller.
        await _purger.PurgeAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var old in stored)
        {
            await DiscardTokensAsync(old.Id).ConfigureAwait(false);
        }

        await _accounts.SaveAccountAsync(incoming, cancellationToken).ConfigureAwait(false);
        _logger.Information(
            "Switched from {OldCount} stored account(s) to {AccountId}; deleted {Mutations} pending change(s), {Commands} AI command(s), {Rules} lunar rule(s) (ADR-0029).",
            stored.Count,
            incoming.Id,
            counts.PendingMutations,
            counts.PendingAiCommands,
            counts.LunarRules);
        return AccountSignInOutcome.Switched;
    }

    /// <summary>Revokes (best effort) and deletes the refresh token of <paramref name="accountId"/>.</summary>
    private async Task DiscardTokensAsync(string accountId)
    {
        var key = KnownSecretKeys.GoogleRefreshToken(accountId);
        try
        {
            var token = await _secrets.GetSecretAsync(key).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                await _oauthClient.RevokeTokenAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Offline or Google unreachable: the token is still deleted locally below (SEC-OAUTH-3).
            _logger.Warning(ex, "Could not revoke the refresh token of account {AccountId}; deleting it locally.", accountId);
        }

        await _secrets.DeleteSecretAsync(key).ConfigureAwait(false);
    }
}
