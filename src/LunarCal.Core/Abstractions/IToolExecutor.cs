using LunarCal.Core.Chat;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// FR-CHAT-1, FR-CHAT-2, SEC-PI-2: Abstraction for executing validated calendar tools.
/// Isolates the chat domain and orchestrator from concrete storage / API services.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a validated tool call.
    /// </summary>
    /// <param name="toolCall">The validated tool call to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the tool execution.</returns>
    Task<ToolExecutionResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken = default);
}
