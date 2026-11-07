namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral chat request sent to <see cref="LunarCal.Core.Abstractions.IChatModelClient"/>.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>
    /// Optional system instruction specifying the behavior of the model.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// The conversation history or current messages for the turn.
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();

    /// <summary>
    /// The tool declarations available for the model to invoke.
    /// </summary>
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = Array.Empty<ToolDefinition>();

    /// <summary>
    /// Optional override of the target model ID (uses default configured model if omitted).
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Maximum output tokens allowed for the response.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Opaque multi-turn state (e.g. thought signatures or provider state for clarification turns, spec §7).
    /// </summary>
    public string? ProviderState { get; init; }
}
