namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for securely wiping all local application data (cache, tokens, keys, outbox) per SEC-DATA-3.
/// </summary>
public interface ILocalDataWipeService
{
    /// <summary>
    /// Deletes all local cached files, SQLCipher database, DPAPI keys/tokens, and data lake outbox files.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task WipeAllDataAsync(CancellationToken cancellationToken = default);
}
