namespace LunarCal.Core.Chat;

/// <summary>
/// Severity outcome of local business validation for calendar commands (FR-CONFIRM-5).
/// </summary>
public enum AiBusinessValidationSeverity
{
    /// <summary>
    /// Validation succeeded with zero warnings or errors.
    /// </summary>
    Pass,

    /// <summary>
    /// Validation identified warnings (e.g. past events or schedule conflicts).
    /// </summary>
    Warning,

    /// <summary>
    /// Validation found blocking business logic errors (e.g. end time before start time).
    /// </summary>
    Error
}
