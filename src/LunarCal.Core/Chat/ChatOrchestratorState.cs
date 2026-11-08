namespace LunarCal.Core.Chat;

/// <summary>
/// FR-CHAT-1, FR-CHAT-2, SEC-PI-2: State machine states for chat command orchestration.
/// Idle -> CallingModel -> Validating -> AwaitingConfirmation / AwaitingClarification -> Executing -> Completed / Failed / Cancelled.
/// </summary>
public enum ChatOrchestratorState
{
    /// <summary>
    /// Initial idle state waiting for user input.
    /// </summary>
    Idle,

    /// <summary>
    /// System prompt rendered and request dispatched to the chat model.
    /// </summary>
    CallingModel,

    /// <summary>
    /// Model responded with tool call(s); validator is validating schema and security constraints.
    /// </summary>
    Validating,

    /// <summary>
    /// Model requested clarification (e.g. missing time, ambiguous date, FR-CONFIRM-8).
    /// </summary>
    AwaitingClarification,

    /// <summary>
    /// Tool call is validated and awaiting explicit user confirmation (FR-CHAT-1, FR-CHAT-2, SEC-PI-2).
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    /// Tool call confirmed (or auto-confirmed when permitted) and being executed by tool executor.
    /// </summary>
    Executing,

    /// <summary>
    /// Dialogue turn and tool execution completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Orchestration failed due to validation error, model safety block, or execution error.
    /// </summary>
    Failed,

    /// <summary>
    /// The turn or tool execution was cancelled by the user.
    /// </summary>
    Cancelled
}
