namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral representation of a message in a conversation.
/// </summary>
public sealed class ChatMessage
{
    public ChatMessageRole Role { get; }
    public string? Text { get; }
    public IReadOnlyList<ToolCall> ToolCalls { get; }
    public IReadOnlyList<ToolResult> ToolResults { get; }

    public ChatMessage(
        ChatMessageRole role,
        string? text = null,
        IReadOnlyList<ToolCall>? toolCalls = null,
        IReadOnlyList<ToolResult>? toolResults = null)
    {
        Role = role;
        Text = text;
        ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
        ToolResults = toolResults ?? Array.Empty<ToolResult>();
    }

    public static ChatMessage CreateUser(string text) =>
        new(ChatMessageRole.User, text: text);

    public static ChatMessage CreateSystem(string text) =>
        new(ChatMessageRole.System, text: text);

    public static ChatMessage CreateModel(string? text, IReadOnlyList<ToolCall>? toolCalls = null) =>
        new(ChatMessageRole.Model, text: text, toolCalls: toolCalls);

    public static ChatMessage CreateToolResult(string toolName, string resultJson, string? toolCallId = null) =>
        new(ChatMessageRole.Tool, toolResults: new[] { new ToolResult(toolName, resultJson, toolCallId) });
}
