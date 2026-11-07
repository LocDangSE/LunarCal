namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7, FR-DATA-3: Vendor-neutral token consumption metrics for telemetry and data collection.
/// </summary>
/// <param name="PromptTokens">Number of tokens in the prompt / context.</param>
/// <param name="ResponseTokens">Number of tokens generated in the response.</param>
/// <param name="TotalTokens">Total tokens consumed.</param>
public sealed record ChatTokenUsage(
    int PromptTokens,
    int ResponseTokens,
    int TotalTokens);
