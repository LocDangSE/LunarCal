namespace LunarCal.Core.Chat;

/// <summary>
/// Status codes indicating the result of tool call parameter validation (FR-CHAT-3, SEC-PI-2).
/// </summary>
public enum ToolValidationCode
{
    /// <summary>Tool call arguments are fully valid according to schema and security policies.</summary>
    Valid = 0,

    /// <summary>The tool name is unknown or not supported.</summary>
    UnknownTool = 1,

    /// <summary>The tool arguments could not be parsed as valid JSON.</summary>
    InvalidJson = 2,

    /// <summary>One or more required schema parameters are missing or empty.</summary>
    MissingRequiredParameter = 3,

    /// <summary>A string argument exceeded the maximum allowed length (4000 characters).</summary>
    StringLengthExceeded = 4,

    /// <summary>Bulk create request exceeded the maximum allowed events limit (500 events).</summary>
    BulkLimitExceeded = 5,

    /// <summary>The total number of tool calls in the session exceeded the maximum quota (50 calls).</summary>
    SessionCallLimitExceeded = 6,

    /// <summary>A date range or recurrence window exceeded the maximum duration (5 years).</summary>
    TimespanLimitExceeded = 7,

    /// <summary>Start date is after end date or date values are invalid.</summary>
    InvalidDateRange = 8,

    /// <summary>Lunar date parameters are out of acceptable bounds (lunarDay: 1-30, lunarMonth: 1-12, yearsAhead: 1-20).</summary>
    InvalidLunarParameters = 9,

    /// <summary>Scope value is invalid (must be 'this' or 'all').</summary>
    InvalidScope = 10,

    /// <summary>Security policy violation detected.</summary>
    PolicyViolation = 11
}

/// <summary>
/// Outcome of validating a model tool invocation request against schema and security policies (FR-CHAT-3, SEC-PI-2).
/// </summary>
public sealed record ToolValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    ToolValidationCode Code = ToolValidationCode.Valid)
{
    /// <summary>Creates a successful validation result.</summary>
    public static ToolValidationResult Success() => new(true, null, ToolValidationCode.Valid);

    /// <summary>Creates a failed validation result with an error message and code.</summary>
    public static ToolValidationResult Failure(string errorMessage, ToolValidationCode code) =>
        new(false, errorMessage, code);
}
