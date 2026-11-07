namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for executing Google account sign-out: revokes OAuth tokens on Google, deletes DPAPI tokens,
/// and purges cached account data and events from local SQLite storage (FR-AUTH-4, SEC-OAUTH-3).
/// </summary>
public interface IGoogleSignOutService
{
    /// <summary>
    /// Signs out the specified Google account, revoking remote tokens, deleting encrypted tokens from secret store,
    /// and purging all cached calendars and events for the account from local database storage (FR-AUTH-4).
    /// </summary>
    /// <param name="accountId">Unique Google account ID ('sub').</param>
    /// <param name="confirmed">Confirmation flag ensuring user has explicitly confirmed sign-out (FR-AUTH-4).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of sign-out operation.</returns>
    Task<GoogleSignOutResult> SignOutAsync(string accountId, bool confirmed, CancellationToken cancellationToken = default);
}
