namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CHAT-1, FR-CHAT-5, FR-QUEUE-9: Result of executing a calendar tool call.
/// </summary>
public sealed record ToolExecutionResult
{
    /// <summary>
    /// Indicates whether the tool execution succeeded.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Output message or feedback for the user / model.
    /// </summary>
    public string? OutputMessage { get; init; }

    /// <summary>
    /// Detailed error message if the execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// IDs of events created, updated, or deleted by the tool call.
    /// </summary>
    public IReadOnlyList<string>? EventIds { get; init; }

    /// <summary>
    /// Batch identifier for undo operations (FR-CHAT-5, FR-QUEUE-9).
    /// </summary>
    public string? BatchId { get; init; }

    /// <summary>
    /// Creates a successful tool execution result.
    /// </summary>
    public static ToolExecutionResult Success(string? outputMessage = null, IReadOnlyList<string>? eventIds = null, string? batchId = null) =>
        new()
        {
            IsSuccess = true,
            OutputMessage = outputMessage,
            EventIds = eventIds,
            BatchId = batchId
        };

    /// <summary>
    /// Creates a failed tool execution result.
    /// </summary>
    public static ToolExecutionResult Failure(string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
