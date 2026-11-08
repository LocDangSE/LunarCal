namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// Configuration options for the AI command queue worker service (FR-QUEUE-1..9).
/// </summary>
public sealed class AiQueueWorkerOptions
{
    /// <summary>Polling interval when the queue is idle (default 5 seconds).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Retention period for terminal completed/cancelled commands before automated cleanup (default 30 days, FR-QUEUE-9).</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Base retry delay for exponential backoff (default 1 minute, FR-QUEUE-5).</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}
