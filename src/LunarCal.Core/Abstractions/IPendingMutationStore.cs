using LunarCal.Core.Calendar;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for persisting and querying the pending offline mutations queue (spec §6, FR-SYNC-4, NFR-REL).
/// </summary>
public interface IPendingMutationStore
{
    /// <summary>
    /// Enqueues a new pending mutation.
    /// </summary>
    /// <param name="mutation">The mutation to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueAsync(PendingMutation mutation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves pending mutations in FIFO order (ordered by created_at ascending).
    /// </summary>
    /// <param name="limit">Maximum number of mutations to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PendingMutation>> GetPendingAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single mutation by its identifier.
    /// </summary>
    /// <param name="id">The mutation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PendingMutation?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an attempt failure on a pending mutation, incrementing its attempts counter and storing the error.
    /// </summary>
    /// <param name="id">The mutation identifier.</param>
    /// <param name="errorMessage">The error message encountered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAttemptFailedAsync(string id, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a processed or superseded mutation from the queue.
    /// </summary>
    /// <param name="id">The mutation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of pending mutations currently in the queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
