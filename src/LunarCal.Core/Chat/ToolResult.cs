namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral representation of a tool execution outcome returned to the model.
/// </summary>
/// <param name="ToolName">The name of the tool that produced this result.</param>
/// <param name="ResultJson">JSON-encoded string representing the execution result.</param>
/// <param name="ToolCallId">Optional call identifier matching the originating ToolCall.</param>
public sealed record ToolResult(
    string ToolName,
    string ResultJson,
    string? ToolCallId = null);
