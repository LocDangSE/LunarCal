using System;

namespace LunarCal.Core.Chat;

/// <summary>
/// A specific message produced during local business validation (FR-CONFIRM-5).
/// </summary>
/// <param name="Severity">Severity of the message.</param>
/// <param name="Message">Human-readable description of the validation issue or notice.</param>
/// <param name="ConflictEventTitle">Optional title of conflicting event if conflict detected.</param>
/// <param name="ConflictStart">Optional start of conflicting event.</param>
/// <param name="ConflictEnd">Optional end of conflicting event.</param>
public sealed record AiBusinessValidationMessage(
    AiBusinessValidationSeverity Severity,
    string Message,
    string? ConflictEventTitle = null,
    DateTimeOffset? ConflictStart = null,
    DateTimeOffset? ConflictEnd = null);
