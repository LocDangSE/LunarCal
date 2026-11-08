namespace LunarCal.Core.Chat;

/// <summary>
/// FR-QUEUE-1, FR-QUEUE-2, FR-QUEUE-4, FR-QUEUE-8, FR-QUEUE-9: AI command entity stored in SQLite.
/// Tracks prompt text, relative timestamp anchor (<c>TypedAt</c>), 6-state progression, retry attempts,
/// idempotent request key, and undo batch ID.
/// </summary>
public sealed record AiCommand
{
    /// <summary>Unique identifier (UUID).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable display number (e.g. 1000, 1001).</summary>
    public required int DisplayNo { get; init; }

    /// <summary>Formatted display code (e.g. "#1000", FR-QUEUE-1).</summary>
    public string DisplayCode => $"#{DisplayNo}";

    /// <summary>Original natural language command text input by the user.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Timestamp when the user typed the command, anchored to user timezone (FR-QUEUE-4).
    /// Used by the model to evaluate relative time terms such as "tomorrow" or "next week".
    /// </summary>
    public required DateTimeOffset TypedAt { get; init; }

    /// <summary>Timezone identifier at the time of typing (e.g. "SE Asia Standard Time").</summary>
    public required string TimeZone { get; init; }

    /// <summary>Detected input language ("vi", "en", or "mixed").</summary>
    public required string InputLang { get; init; }

    /// <summary>
    /// Current queue lifecycle state (FR-QUEUE-2, see <see cref="AiCommandStatus"/>).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Number of retries attempted so far (0 to 5, FR-QUEUE-5).</summary>
    public int RetryCount { get; init; }

    /// <summary>Scheduled timestamp for the next retry attempt if in error backoff state.</summary>
    public DateTimeOffset? NextRetryAt { get; init; }

    /// <summary>Detailed description of the last error encountered.</summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Identifier of the batch created for undo operations (FR-CHAT-5, FR-QUEUE-9).
    /// </summary>
    public string? BatchId { get; init; }

    /// <summary>
    /// The model's proposal waiting for the user (tool call or clarification question), null before the model answered
    /// (FR-QUEUE-2, FR-CONFIRM-1, FR-CONFIRM-8, ADR-0031).
    /// </summary>
    public AiCommandProposal? Proposal { get; init; }

    /// <summary>Timestamp when this command was initially saved to the queue.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Timestamp of the last status or error update.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// FR-QUEUE-8: Deterministic idempotency key for calendar event creation
    /// stored in <c>extendedProperties.private.requestId</c> to prevent duplicates on retries.
    /// </summary>
    public string RequestId => $"lunarcal-ai-{Id}";
}
