namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CHAT-2, FR-CHAT-3: Configuration options for <see cref="ChatOrchestrator"/>.
/// </summary>
public sealed class ChatOrchestratorOptions
{
    /// <summary>
    /// FR-CHAT-2: Flag allowing single event creation (<c>create_event</c>) to be executed
    /// automatically without requiring manual confirmation. Defaults to <c>false</c>.
    /// <para>
    /// Note: Deletion (<c>delete_event</c>), bulk creation (<c>bulk_create_events</c>),
    /// and recurring modifications ALWAYS require manual confirmation regardless of this setting (SEC-PI-2).
    /// </para>
    /// </summary>
    public bool AutoConfirmSingleEvent { get; set; }

    /// <summary>
    /// FR-CHAT-3: Maximum number of tool calls permitted per chat session (default 50).
    /// </summary>
    public int MaxToolCallsPerSession { get; set; } = ToolDefinitions.MaxToolCallsPerSession;

    /// <summary>
    /// Optional override for the model identifier to send in chat requests.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Version of the system prompt template to render (spec §7, ADR-0030). Defaults to "v1".
    /// </summary>
    public string PromptVersion { get; set; } = SystemPromptProvider.V1;
}
