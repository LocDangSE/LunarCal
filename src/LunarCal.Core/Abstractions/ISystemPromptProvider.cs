using LunarCal.Core.Chat;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Provides versioned system prompts and context injection for the AI chat orchestrator (FR-CHAT-1, FR-CHAT-6, FR-QUEUE-4, spec §7, ADR-0030).
/// </summary>
public interface ISystemPromptProvider
{
    /// <summary>
    /// Gets the default system prompt version (e.g. "v1").
    /// </summary>
    string DefaultVersion { get; }

    /// <summary>
    /// Checks whether a specific prompt version exists.
    /// </summary>
    bool HasVersion(string version);

    /// <summary>
    /// Retrieves the unrendered raw prompt template for a specific version.
    /// </summary>
    string GetRawTemplate(string? version = null);

    /// <summary>
    /// Renders the system prompt by replacing contextual variables with the provided <see cref="PromptContext"/>.
    /// </summary>
    string RenderSystemPrompt(PromptContext context, string? version = null);
}
