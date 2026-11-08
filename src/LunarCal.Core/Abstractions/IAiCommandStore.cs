using LunarCal.Core.Chat;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// FR-QUEUE-1, FR-QUEUE-2, FR-QUEUE-8, FR-QUEUE-9: Port for persisting and managing AI command queue items in SQLite.
/// </summary>
public interface IAiCommandStore
{
    /// <summary>
    /// Enqueues a new natural language command immediately upon input (FR-QUEUE-1).
    /// Allocates an auto-incrementing <c>DisplayNo</c> (starting at 1000).
    /// </summary>
    Task<AiCommand> EnqueueAsync(
        string text,
        DateTimeOffset typedAt,
        string timeZone,
        string inputLang,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a command by its unique ID.
    /// </summary>
    Task<AiCommand?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves commands ready for processing in FIFO order:
    /// status is <c>pending</c> or (<c>error</c> with <c>RetryCount &lt; 5</c> and <c>NextRetryAt &lt;= now</c>).
    /// </summary>
    Task<IReadOnlyList<AiCommand>> GetPendingQueueAsync(
        DateTimeOffset now,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all recent commands for display in the AI Queue Drawer (FR-QUEUE-2, Figma 03).
    /// </summary>
    Task<IReadOnlyList<AiCommand>> GetAllAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status and operational metadata of an AI command.
    /// </summary>
    Task UpdateStatusAsync(
        string id,
        string status,
        string? lastError = null,
        int? retryCount = null,
        DateTimeOffset? nextRetryAt = null,
        string? batchId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-QUEUE-8: Crash recovery. Resets any commands stuck in <c>processing</c> back to <c>pending</c>.
    /// </summary>
    Task<int> RecoverProcessingCommandsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-QUEUE-9: Deletes terminal commands (<c>completed</c> and <c>cancelled</c>) updated before the cutoff timestamp.
    /// </summary>
    Task<int> CleanupOldCommandsAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of non-terminal commands currently in the queue (excluding completed and cancelled).
    /// </summary>
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a command, transitioning it to the <c>cancelled</c> terminal status.
    /// </summary>
    Task CancelAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately requeues an error command for retry, clearing its backoff timer.
    /// </summary>
    Task RetryNowAsync(string id, CancellationToken cancellationToken = default);
}
