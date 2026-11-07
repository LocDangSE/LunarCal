namespace LunarCal.Core.Abstractions;

/// <summary>
/// Abstraction for system clock and asynchronous time delays (FR-SYNC-5).
/// Enables deterministic testing of time-dependent components and rate limiters without real-time delays.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current date and time in Coordinated Universal Time (UTC).
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Asynchronously waits for the specified time span.
    /// </summary>
    /// <param name="delay">The time span to wait.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}
