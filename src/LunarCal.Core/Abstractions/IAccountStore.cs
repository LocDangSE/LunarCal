using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Store for managing authenticated accounts in local storage (spec §6 Account table, FR-AUTH-1, FR-AUTH-4).
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// Gets the primary authenticated account, or null if no account is signed in.
    /// </summary>
    Task<AccountInfo?> GetPrimaryAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored accounts.
    /// </summary>
    Task<IReadOnlyList<AccountInfo>> GetAllAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates account information.
    /// </summary>
    Task SaveAccountAsync(AccountInfo account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an account by ID.
    /// </summary>
    Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default);
}
