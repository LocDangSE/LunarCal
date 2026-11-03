namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of a Google account sign-out operation (FR-AUTH-4).
/// </summary>
public sealed record GoogleSignOutResult
{
    /// <summary>Whether local account wipe and sign-out completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Whether the OAuth token was successfully revoked on Google's servers.</summary>
    public bool TokenRevokedOnServer { get; init; }

    /// <summary>Whether sign-out was aborted due to lack of confirmation.</summary>
    public bool CanceledOrUnconfirmed { get; init; }

    /// <summary>Optional error message if an operation failed.</summary>
    public string? Error { get; init; }

    /// <summary>Creates a successful sign-out result.</summary>
    public static GoogleSignOutResult Succeeded(bool tokenRevokedOnServer = true) =>
        new()
        {
            Success = true,
            TokenRevokedOnServer = tokenRevokedOnServer
        };

    /// <summary>Creates an unconfirmed sign-out result.</summary>
    public static GoogleSignOutResult Unconfirmed() =>
        new()
        {
            Success = false,
            CanceledOrUnconfirmed = true,
            Error = "Sign-out was not confirmed by user."
        };

    /// <summary>Creates a failed sign-out result.</summary>
    public static GoogleSignOutResult Failed(string error, bool tokenRevokedOnServer = false) =>
        new()
        {
            Success = false,
            TokenRevokedOnServer = tokenRevokedOnServer,
            Error = error
        };
}
