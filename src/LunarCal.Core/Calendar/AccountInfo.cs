namespace LunarCal.Core.Calendar;

/// <summary>
/// User account information stored in local storage (spec §6 Account table, FR-AUTH-1, FR-AUTH-4).
/// </summary>
public sealed record AccountInfo
{
    /// <summary>Google user account ID ('sub' claim) or local identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Google account email address.</summary>
    public required string Email { get; init; }

    /// <summary>User display name.</summary>
    public string? DisplayName { get; init; }
}
