namespace LunarCal.Core.Chat;

/// <summary>
/// Result of evaluating local heuristic confidence for an AI tool call (FR-CONFIRM-4).
/// </summary>
/// <param name="Score">Heuristic confidence percentage between 0 and 100.</param>
/// <param name="Level">Categorical level (High >= 90%, Medium 60-89%, Low &lt; 60%).</param>
/// <param name="Reasons">Detailed breakdown of heuristic penalty and score deductions.</param>
public sealed record AiConfidenceResult(
    int Score,
    AiConfidenceLevel Level,
    IReadOnlyList<string> Reasons);
