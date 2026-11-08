using System;
using System.Threading;
using System.Threading.Tasks;
using LunarCal.Core.Abstractions;

namespace LunarCal.Core.Chat;

/// <summary>
/// Port for running local business logic validations on AI-generated tool calls against SQLite cache (FR-CONFIRM-5).
/// Verifies valid time ranges, warns for past events, detects schedule conflicts, and enforces safety bounds.
/// User calendar data is NEVER sent to cloud LLM for conflict checks (NFR-PRIV).
/// </summary>
public interface IAiBusinessValidator
{
    /// <summary>
    /// Validates an AI tool call against business rules and the local event store.
    /// </summary>
    /// <param name="toolCall">The tool call to validate.</param>
    /// <param name="eventStore">Local SQLite event store containing cached user events.</param>
    /// <param name="typedAt">Timestamp anchor when the user typed the request.</param>
    /// <param name="targetCalendarId">Optional destination calendar identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="AiBusinessValidationResult"/> with severity and diagnostic messages.</returns>
    Task<AiBusinessValidationResult> ValidateAsync(
        ToolCall toolCall,
        IEventStore eventStore,
        DateTimeOffset typedAt,
        string? targetCalendarId = null,
        CancellationToken cancellationToken = default);
}
