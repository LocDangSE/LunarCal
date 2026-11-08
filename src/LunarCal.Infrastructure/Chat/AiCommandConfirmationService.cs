using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Serilog;

namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-CHAT-1, FR-CHAT-2, FR-CONFIRM-6, FR-CONFIRM-8, SEC-PI-2: Executes the proposal stored on a queued command after
/// the user confirmed it. The guard on <see cref="AiCommandStatus.NeedsConfirmation"/> is what makes sure nothing is
/// ever written for a command the user has not seen (ADR-0031).
/// </summary>
public sealed class AiCommandConfirmationService : IAiCommandConfirmationService
{
    private readonly IAiCommandStore _store;
    private readonly IToolExecutor _executor;
    private readonly ILogger _logger;

    public AiCommandConfirmationService(IAiCommandStore store, IToolExecutor executor, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? Log.ForContext<AiCommandConfirmationService>();
    }

    /// <inheritdoc />
    public async Task<ToolExecutionResult> ConfirmAsync(string commandId, ToolCall? editedCall = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        var command = await _store.GetByIdAsync(commandId, cancellationToken).ConfigureAwait(false);
        if (command is null || command.Status != AiCommandStatus.NeedsConfirmation)
        {
            return ToolExecutionResult.Failure("The command is not waiting for confirmation.");
        }

        var call = editedCall ?? command.Proposal?.ToolCall;
        if (call is null)
        {
            return ToolExecutionResult.Failure("The command has no proposed action to confirm.");
        }

        var validation = ToolCallValidator.Validate(call, 0);
        if (!validation.IsValid)
        {
            return ToolExecutionResult.Failure(validation.ErrorMessage ?? "The action is not valid.");
        }

        // The command's request id makes a repeated confirmation overwrite the same event instead of duplicating it (FR-QUEUE-8).
        var idempotentCall = call with { Id = command.RequestId };

        ToolExecutionResult result;
        try
        {
            result = await _executor.ExecuteAsync(idempotentCall, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Executing confirmed AI command {CommandId} failed", commandId);
            result = ToolExecutionResult.Failure(ex.Message);
        }

        if (result.IsSuccess)
        {
            await _store.SetProposalAsync(commandId, new AiCommandProposal(call), cancellationToken).ConfigureAwait(false);
            await _store.UpdateStatusAsync(commandId, AiCommandStatus.Completed, batchId: result.BatchId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _store.UpdateStatusAsync(
                commandId,
                AiCommandStatus.Error,
                lastError: result.ErrorMessage ?? "Tool execution failed.",
                retryCount: command.RetryCount,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SubmitClarificationAsync(string commandId, string answer, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        var command = await _store.GetByIdAsync(commandId, cancellationToken).ConfigureAwait(false);
        if (command is null || command.Status != AiCommandStatus.NeedsConfirmation)
        {
            return;
        }

        var question = command.Proposal?.ClarificationQuestion ?? command.LastError;
        await _store.SetProposalAsync(commandId, new AiCommandProposal(null, question, answer.Trim()), cancellationToken).ConfigureAwait(false);
        await _store.RetryNowAsync(commandId, cancellationToken).ConfigureAwait(false);
    }
}
