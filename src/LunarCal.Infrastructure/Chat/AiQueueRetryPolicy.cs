namespace LunarCal.Infrastructure.Chat;

/// <summary>
/// FR-QUEUE-5: Exponential backoff with jitter retry policy for AI command queue processing.
/// Up to 5 retries. Beyond 5 retries, transitions to unretryable error waiting for manual action.
/// </summary>
public sealed class AiQueueRetryPolicy
{
    public const int MaxRetries = 5;

    private readonly TimeSpan _baseDelay;
    private readonly Random _random;

    /// <summary>
    /// Initializes a new instance of <see cref="AiQueueRetryPolicy"/>.
    /// </summary>
    /// <param name="baseDelay">Base delay before backoff exponentiation (defaults to 1 minute).</param>
    /// <param name="seed">Optional random seed for deterministic testing.</param>
    public AiQueueRetryPolicy(TimeSpan? baseDelay = null, int? seed = null)
    {
        _baseDelay = baseDelay ?? TimeSpan.FromMinutes(1);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Calculates the next scheduled retry timestamp given the retry attempt number.
    /// Returns <c>null</c> if the attempt number reaches or exceeds <see cref="MaxRetries"/>.
    /// </summary>
    /// <param name="retryCount">The 1-based attempt count (1 to 5).</param>
    /// <param name="now">The current time anchor.</param>
    /// <returns>Next retry timestamp, or null if maximum retries reached.</returns>
    public DateTimeOffset? CalculateNextRetry(int retryCount, DateTimeOffset now)
    {
        if (retryCount >= MaxRetries)
        {
            return null;
        }

        // Exponential backoff: base * 2^(retryCount - 1)
        var factor = Math.Pow(2, Math.Max(0, retryCount - 1));
        var delayMs = _baseDelay.TotalMilliseconds * factor;

        // Jitter: add uniform random variation between 0 and 20% of delay (or at least 0-5 seconds)
        var maxJitterMs = Math.Max(5000, delayMs * 0.2);
        var jitterMs = _random.NextDouble() * maxJitterMs;

        var totalDelay = TimeSpan.FromMilliseconds(delayMs + jitterMs);
        return now.Add(totalDelay);
    }
}
