using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Extension methods for configuring resilient and secure HTTP pipelines (SEC-NET-1, SEC-NET-2, FR-SYNC-5).
/// </summary>
public static class ResiliencePipelineExtensions
{
    public const string DefaultClientName = "LunarCal";

    /// <summary>
    /// Configures standard HTTP resilience with custom idempotency-aware retry filtering (SEC-NET-2)
    /// and automatic exponential backoff with jitter for HTTP 429 and HTTP 403 rateLimitExceeded (FR-SYNC-5).
    /// Prevents retries on write requests that lack a requestId or ETag.
    /// </summary>
    public static IHttpClientBuilder AddLunarCalResilienceHandler(
        this IHttpClientBuilder builder,
        NetworkOptions? networkOptions = null,
        Action<HttpStandardResilienceOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = networkOptions ?? new NetworkOptions();

        builder.AddStandardResilienceHandler(resilienceOptions =>
        {
            // SEC-NET-2, FR-SYNC-5: Bounded retry, exponential backoff, jitter
            resilienceOptions.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
            resilienceOptions.Retry.Delay = options.InitialRetryDelay;
            resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
            resilienceOptions.Retry.UseJitter = true;

            // SEC-NET-2: Timeouts
            resilienceOptions.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;
            resilienceOptions.AttemptTimeout.Timeout = options.AttemptTimeout;

            // SEC-NET-2: Circuit breaker
            resilienceOptions.CircuitBreaker.SamplingDuration = options.CircuitBreakerSamplingDuration;
            resilienceOptions.CircuitBreaker.FailureRatio = options.CircuitBreakerFailureRatio;
            resilienceOptions.CircuitBreaker.MinimumThroughput = options.CircuitBreakerMinimumThroughput;
            resilienceOptions.CircuitBreaker.BreakDuration = options.CircuitBreakerBreakDuration;

            // Allow caller customizations (e.g. In tests for short timeouts/delays)
            configureOptions?.Invoke(resilienceOptions);

            // SEC-NET-2, FR-SYNC-5: Idempotency filter wrapper with Google quota/rate limit retry support (429, 403 rateLimitExceeded)
            var previousShouldHandle = resilienceOptions.Retry.ShouldHandle;
            resilienceOptions.Retry.ShouldHandle = async args =>
            {
                var outcome = args.Outcome;
                if (outcome.Result is HttpResponseMessage response)
                {
                    // FR-SYNC-5: 429 Too Many Requests -> retry with exponential backoff & jitter if safe/idempotent
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var request = args.Context.GetRequestMessage() ?? response.RequestMessage;
                        return HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(request);
                    }

                    // FR-SYNC-5: 403 Forbidden with rateLimitExceeded -> retry with exponential backoff & jitter if safe/idempotent
                    if (response.StatusCode == HttpStatusCode.Forbidden && await IsRateLimitExceeded403Async(response).ConfigureAwait(false))
                    {
                        var request = args.Context.GetRequestMessage() ?? response.RequestMessage;
                        return HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(request);
                    }
                }

                var shouldRetry = await previousShouldHandle(args).ConfigureAwait(false);
                if (!shouldRetry)
                {
                    return false;
                }

                var req = args.Context.GetRequestMessage() ?? args.Outcome.Result?.RequestMessage;
                return HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);
            };
        });

        return builder;
    }

    /// <summary>
    /// Checks whether an HTTP 403 Forbidden response contains a Google rate limit error code (FR-SYNC-5).
    /// </summary>
    public static async ValueTask<bool> IsRateLimitExceeded403Async(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden || response.Content == null)
        {
            return false;
        }

        try
        {
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return content.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a secure SocketsHttpHandler with default OS certificate validation (SEC-NET-1).
    /// Does not bypass or disable certificate validation.
    /// </summary>
    public static SocketsHttpHandler CreateSecureSocketsHttpHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All
        };
    }
}
