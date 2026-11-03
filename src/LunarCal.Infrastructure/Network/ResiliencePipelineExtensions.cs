using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Extension methods for configuring resilient and secure HTTP pipelines (SEC-NET-1, SEC-NET-2).
/// </summary>
public static class ResiliencePipelineExtensions
{
    public const string DefaultClientName = "LunarCal";

    /// <summary>
    /// Configures standard HTTP resilience with custom idempotency-aware retry filtering (SEC-NET-2).
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
            // SEC-NET-2: Bounded retry, exponential backoff, jitter
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

            // SEC-NET-2: Idempotency filter wrapper - write requests without requestId or ETag are NEVER retried
            var previousShouldHandle = resilienceOptions.Retry.ShouldHandle;
            resilienceOptions.Retry.ShouldHandle = async args =>
            {
                var shouldRetry = await previousShouldHandle(args).ConfigureAwait(false);
                if (!shouldRetry)
                {
                    return false;
                }

                var request = args.Context.GetRequestMessage() ?? args.Outcome.Result?.RequestMessage;
                return HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(request);
            };
        });

        return builder;
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
