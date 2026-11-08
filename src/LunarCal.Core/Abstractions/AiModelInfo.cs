namespace LunarCal.Core.Abstractions;

/// <summary>
/// Information about a chat model available for natural language processing (FR-BYOK-5, ADR-0016).
/// </summary>
/// <param name="Id">Model identifier (e.g. "gemini-3.8-flash").</param>
/// <param name="DisplayName">User-friendly display name (e.g. "Gemini 3.8 Flash").</param>
/// <param name="Description">Short description of model capabilities.</param>
/// <param name="InputTokenLimit">Maximum input context window in tokens, if known.</param>
/// <param name="OutputTokenLimit">Maximum output token limit, if known.</param>
/// <param name="IsDefault">Whether this model is the application's configured default.</param>
/// <param name="IsRecommended">Whether this model is in the recommended set for LunarCal.</param>
public record AiModelInfo(
    string Id,
    string DisplayName,
    string? Description = null,
    int? InputTokenLimit = null,
    int? OutputTokenLimit = null,
    bool IsDefault = false,
    bool IsRecommended = false);
