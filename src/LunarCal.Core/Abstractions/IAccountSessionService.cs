using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>How a successful Google sign-in relates to the account already stored on this machine (ADR-0029).</summary>
public enum AccountSignInKind
{
    /// <summary>No account is stored yet.</summary>
    FirstAccount,

    /// <summary>The Google user id equals the stored account (for example a re-login after the token expired).</summary>
    SameAccount,

    /// <summary>The Google user id differs from the stored account: the old data must be wiped first.</summary>
    DifferentAccount,
}

/// <summary>What <see cref="IAccountSessionService.ApplySignInAsync"/> did with a sign-in.</summary>
public enum AccountSignInOutcome
{
    /// <summary>First or same account: the account was saved and nothing was deleted.</summary>
    Saved,

    /// <summary>A different account was confirmed: the old data was deleted and the new account saved.</summary>
    Switched,

    /// <summary>The user declined to switch: the new tokens were discarded and the old state is untouched.</summary>
    Cancelled,
}

/// <summary>
/// Facts shown to the user before a different account replaces the stored one (ADR-0029, Figma S14 1.5).
/// </summary>
public sealed record AccountSignInAssessment
{
    /// <summary>Relation of the signed-in account to the stored one.</summary>
    public required AccountSignInKind Kind { get; init; }

    /// <summary>The account stored on this machine, if any.</summary>
    public AccountInfo? CurrentAccount { get; init; }

    /// <summary>The account that just signed in.</summary>
    public required AccountInfo IncomingAccount { get; init; }

    /// <summary>Calendar changes made offline that were not yet sent to Google and would be lost.</summary>
    public int PendingChanges { get; init; }

    /// <summary>AI commands that were not finished and would be lost.</summary>
    public int PendingAiCommands { get; init; }

    /// <summary>Lunar recurrence rules that exist only on this machine and would be lost.</summary>
    public int LocalLunarRules { get; init; }

    /// <summary>True when switching deletes anything the user could still care about.</summary>
    public bool HasDataToLose => PendingChanges > 0 || PendingAiCommands > 0 || LocalLunarRules > 0;
}

/// <summary>
/// The app works with exactly one Google account at a time (ADR-0029, FR-AUTH-5). Every sign-in path goes through this
/// service so that signing in with another Google account never silently adds a second account.
/// </summary>
public interface IAccountSessionService
{
    /// <summary>
    /// Records a completed Google sign-in. Same or first account: saves it and keeps all data. Different account: asks
    /// <paramref name="confirmSwitch"/>; when it returns <see langword="true"/> the old tokens and local data are deleted
    /// and the new account is saved, otherwise the tokens just issued for the new account are revoked and discarded.
    /// </summary>
    /// <param name="signedInUser">The user returned by the OAuth flow. Its refresh token is already in the secret store.</param>
    /// <param name="confirmSwitch">Shows the confirmation (what will be lost) and returns the user's decision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AccountSignInOutcome> ApplySignInAsync(
        GoogleUserInfo signedInUser,
        Func<AccountSignInAssessment, CancellationToken, Task<bool>> confirmSwitch,
        CancellationToken cancellationToken = default);
}
