namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Configuration options for outbound network requests and HTTP resilience (SEC-NET-1, SEC-NET-2).
/// Bound from configuration section "LunarCal:Network".
/// </summary>
public sealed class NetworkOptions
{
    public const string SectionName = "LunarCal:Network";

    /// <summary>
    /// Explicitly allow-listed host patterns (SEC-NET-1).
    /// Supports exact hostnames (e.g. "accounts.google.com") and wildcard domain prefixes (e.g. "*.googleapis.com").
    /// </summary>
    public List<string> AllowedHosts { get; set; } = new()
    {
        "*.googleapis.com",
        "accounts.google.com",
        "oauth2.googleapis.com",
        "generativelanguage.googleapis.com"
    };

    /// <summary>
    /// Enforces that only HTTPS requests are permitted (SEC-NET-1). Default is true.
    /// </summary>
    public bool EnforceHttpsOnly { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts for transient failures on idempotent requests (SEC-NET-2).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Initial backoff delay for retries.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Overall timeout for the entire request execution including all retry attempts (SEC-NET-2).
    /// </summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Per-attempt timeout (SEC-NET-2).
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Sampling duration for circuit breaker evaluation (SEC-NET-2).
    /// </summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Failure ratio threshold (0.0 to 1.0) to open the circuit breaker.
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Minimum throughput in the sampling window before circuit breaker activates.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration for which circuit breaker stays open when tripped.
    /// </summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
