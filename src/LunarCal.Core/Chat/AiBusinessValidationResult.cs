using System;
using System.Collections.Generic;
using System.Linq;

namespace LunarCal.Core.Chat;

/// <summary>
/// Combined result of local SQLite-based business logic validation (FR-CONFIRM-5).
/// </summary>
public sealed class AiBusinessValidationResult
{
    /// <summary>
    /// Overall severity of the validation run.
    /// </summary>
    public AiBusinessValidationSeverity Severity { get; }

    /// <summary>
    /// Detailed list of validation messages, warnings, and errors.
    /// </summary>
    public IReadOnlyList<AiBusinessValidationMessage> Messages { get; }

    /// <summary>
    /// Indicates whether any blocking business logic error occurred.
    /// </summary>
    public bool HasError => Severity == AiBusinessValidationSeverity.Error;

    /// <summary>
    /// Indicates whether the command passed without blocking errors.
    /// </summary>
    public bool IsValid => !HasError;

    /// <summary>
    /// Initializes a new instance of <see cref="AiBusinessValidationResult"/>.
    /// </summary>
    public AiBusinessValidationResult(
        AiBusinessValidationSeverity severity,
        IReadOnlyList<AiBusinessValidationMessage> messages)
    {
        Severity = severity;
        Messages = messages ?? Array.Empty<AiBusinessValidationMessage>();
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static AiBusinessValidationResult Success(string message = "Đã qua kiểm tra nghiệp vụ: giờ hợp lệ, không xung đột với lịch hiện tại.")
    {
        return new AiBusinessValidationResult(
            AiBusinessValidationSeverity.Pass,
            [new AiBusinessValidationMessage(AiBusinessValidationSeverity.Pass, message)]);
    }

    /// <summary>
    /// Creates a warning validation result.
    /// </summary>
    public static AiBusinessValidationResult WithWarnings(IEnumerable<AiBusinessValidationMessage> warnings)
    {
        var list = warnings.ToList();
        return new AiBusinessValidationResult(
            AiBusinessValidationSeverity.Warning,
            list);
    }

    /// <summary>
    /// Creates an error validation result.
    /// </summary>
    public static AiBusinessValidationResult WithErrors(IEnumerable<AiBusinessValidationMessage> errors)
    {
        var list = errors.ToList();
        return new AiBusinessValidationResult(
            AiBusinessValidationSeverity.Error,
            list);
    }
}
