namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for querying available AI models, selecting a model, and verifying function calling support (FR-BYOK-5, ADR-0016).
/// </summary>
public interface IAiModelService
{
    /// <summary>
    /// Gets the list of available models filtered for compatibility with LunarCal (FR-BYOK-5, ADR-0016 §2).
    /// </summary>
    Task<IReadOnlyList<AiModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently selected model ID, falling back to the configured default model if not explicitly set (FR-BYOK-5).
    /// </summary>
    Task<string> GetSelectedModelIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects and persists the specified model ID after optionally validating its function calling support (FR-BYOK-5).
    /// </summary>
    /// <returns>True if the model was validated and saved; false if function calling validation failed.</returns>
    Task<bool> SetSelectedModelAsync(string modelId, bool validateFunctionCalling = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether the specified model supports function calling by executing a lightweight probe request (FR-BYOK-5).
    /// </summary>
    Task<bool> ValidateFunctionCallingSupportAsync(string modelId, CancellationToken cancellationToken = default);
}
