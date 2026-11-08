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

    /// <summary>
    /// Minimum time between two model calls (default 4 seconds = at most 15 per minute, the Gemini free tier limit).
    /// The worker handles one command per pass, so a backlog (for example commands typed offline) drains at this pace
    /// instead of firing at once and getting rate limited (P4-R10).
    /// </summary>
    public TimeSpan MinCallInterval { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Safety valve: at most this many model calls per rolling hour (default 120). Whatever bug or odd data would make
    /// the worker call the model over and over, it can never burn more than this; the queue pauses and logs a warning
    /// (P4-R10).
    /// </summary>
    public int MaxCallsPerHour { get; set; } = 120;

    /// <summary>How long a command waits after a network failure that happened while the app believed it was online.</summary>
    public TimeSpan NetworkFailureBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay before the first retry (default 5 seconds, FR-QUEUE-5). The user is usually waiting for the answer, so the
    /// first retries are quick: 5 s, 30 s, 3 min, 18 min (see <see cref="AiQueueRetryPolicy"/>).
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
