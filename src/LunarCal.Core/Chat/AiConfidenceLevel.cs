namespace LunarCal.Core.Chat;

/// <summary>
/// Categorical confidence levels for AI natural language commands (FR-CONFIRM-4).
/// Thresholds: High >= 90%, Medium 60-89%, Low &lt; 60%.
/// </summary>
public enum AiConfidenceLevel
{
    /// <summary>
    /// Confidence &lt; 60%: Uncertain; requires user clarification or manual edits before confirming.
    /// </summary>
    Low,

    /// <summary>
    /// Confidence 60% - 89%: Plausible but contains ambiguities or warnings that need review.
    /// </summary>
    Medium,

    /// <summary>
    /// Confidence >= 90%: Clear and fully validated.
    /// </summary>
    High
}
