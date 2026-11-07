using LunarCal.Core.Abstractions;
using Serilog;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// DelegatingHandler that throttles outbound HTTP requests to a configurable maximum rate per second (FR-SYNC-5).
/// Enforces rate limiting (default: &lt;= 5 requests/second) using an <see cref="IClock"/> or TimeProvider abstraction.
/// </summary>
public sealed class ThrottlingDelegatingHandler : DelegatingHandler
{
    private readonly int _maxRequestsPerSecond;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly List<DateTimeOffset> _scheduledTimes = new();

    /// <summary>
    /// Initializes a new instance of <see cref="ThrottlingDelegatingHandler"/>.
    /// </summary>
    /// <param name="maxRequestsPerSecond">Maximum allowed requests per rolling one-second window (default: 5).</param>
    /// <param name="clock">Clock abstraction used for timestamps and asynchronous waiting.</param>
    /// <param name="logger">Optional Serilog logger.</param>
    /// <param name="innerHandler">Optional inner HTTP message handler.</param>
    public ThrottlingDelegatingHandler(
        int maxRequestsPerSecond = 5,
        IClock? clock = null,
        ILogger? logger = null,
        HttpMessageHandler? innerHandler = null)
    {
        if (maxRequestsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRequestsPerSecond),
                maxRequestsPerSecond,
                "Max requests per second must be greater than zero.");
        }

        _maxRequestsPerSecond = maxRequestsPerSecond;
        _clock = clock ?? new SystemClock();
        _logger = logger ?? Log.ForContext<ThrottlingDelegatingHandler>();

        if (innerHandler != null)
        {
            InnerHandler = innerHandler;
        }
    }

    /// <summary>
    /// Gets the configured maximum requests per second.
    /// </summary>
    public int MaxRequestsPerSecond => _maxRequestsPerSecond;

    /// <summary>
    /// Gets the clock used by this handler.
    /// </summary>
    public IClock Clock => _clock;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TimeSpan delay = TimeSpan.Zero;

        lock (_lock)
        {
            var now = _clock.UtcNow;

            // Purge expired reservations older than 1 rolling second
            _scheduledTimes.RemoveAll(t => (now - t) >= TimeSpan.FromSeconds(1));

            DateTimeOffset targetTime;
            if (_scheduledTimes.Count < _maxRequestsPerSecond)
            {
                targetTime = now;
            }
            else
            {
                // Slot is available 1 second after the request that filled capacity
                var referenceTime = _scheduledTimes[_scheduledTimes.Count - _maxRequestsPerSecond];
                targetTime = referenceTime + TimeSpan.FromSeconds(1);
                if (targetTime < now)
                {
                    targetTime = now;
                }
            }

            delay = targetTime - now;
            _scheduledTimes.Add(targetTime);
        }

        if (delay > TimeSpan.Zero)
        {
            _logger.Debug(
                "Throttling HTTP request by {DelayMs:F1} ms to enforce rate limit of {MaxRate} req/s",
                delay.TotalMilliseconds,
                _maxRequestsPerSecond);

            await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
