namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral declaration of a tool available for the model to invoke.
/// </summary>
/// <param name="Name">Tool name matching tool identifier rules (e.g. "create_event").</param>
/// <param name="Description">Human-readable description of what the tool accomplishes.</param>
/// <param name="ParametersSchemaJson">JSON Schema describing the parameters accepted by the tool.</param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersSchemaJson);
