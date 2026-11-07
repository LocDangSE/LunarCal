namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral representation of a tool/function call requested by the model.
/// </summary>
/// <param name="Name">The tool/function name (e.g. "create_event").</param>
/// <param name="ArgumentsJson">The JSON string representing the arguments object passed to the tool.</param>
/// <param name="Id">Optional call identifier used by providers that track call IDs.</param>
public sealed record ToolCall(
    string Name,
    string ArgumentsJson,
    string? Id = null);
