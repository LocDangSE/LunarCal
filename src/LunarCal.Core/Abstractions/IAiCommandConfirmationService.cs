using LunarCal.Core.Chat;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// FR-CHAT-2, FR-CONFIRM-6, FR-CONFIRM-8, SEC-PI-2: The only path through which a queued AI command is written to the
/// calendar. It runs a stored proposal strictly after the user confirmed it in the confirmation dialog.
/// </summary>
public interface IAiCommandConfirmationService
{
    /// <summary>
    /// Executes the stored proposal of a command that is in <c>needs_confirmation</c> (optionally with the user's
    /// edits) and marks the command completed, or failed with the error. Never runs for any other status.
    /// </summary>
    Task<ToolExecutionResult> ConfirmAsync(string commandId, ToolCall? editedCall = null, CancellationToken cancellationToken = default);

    /// <summary>Records the user's answer to the model's question and puts the command back in the queue (FR-CONFIRM-8).</summary>
    Task SubmitClarificationAsync(string commandId, string answer, CancellationToken cancellationToken = default);
}
