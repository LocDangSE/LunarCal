namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Helper to determine whether an HTTP request is idempotent and safe to retry (SEC-NET-2).
/// Read requests (GET, HEAD, OPTIONS, TRACE) are inherently safe.
/// Write requests (POST, PUT, PATCH, DELETE) are only retried if an explicit idempotency identifier
/// (requestId or ETag/If-Match) is provided.
/// </summary>
public static class HttpIdempotencyHelper
{
    public static readonly HttpRequestOptionsKey<string> RequestIdOptionKey = new("LunarCal.RequestId");
    public static readonly HttpRequestOptionsKey<string> ETagOptionKey = new("LunarCal.ETag");

    private static readonly HashSet<string> _safeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "OPTIONS",
        "TRACE"
    };

    private static readonly string[] _requestIdHeaders =
    {
        "X-Request-Id",
        "Request-Id",
        "X-Client-Request-Id",
        "Client-Request-Id"
    };

    private static readonly string[] _etagHeaders =
    {
        "If-Match",
        "If-None-Match",
        "ETag"
    };

    /// <summary>
    /// Evaluates whether the request is idempotent or safe to retry upon transient failure (SEC-NET-2).
    /// </summary>
    public static bool IsIdempotentOrSafeToRetry(HttpRequestMessage? request)
    {
        if (request == null)
        {
            return false;
        }

        // Safe methods (GET, HEAD, etc.) are idempotent and safe to retry
        if (_safeMethods.Contains(request.Method.Method))
        {
            return true;
        }

        // Write methods (POST, PUT, PATCH, DELETE) require requestId or ETag (SEC-NET-2)
        return HasRequestId(request) || HasETag(request);
    }

    /// <summary>
    /// Checks whether the request carries a requestId in options, headers, or query parameters.
    /// </summary>
    public static bool HasRequestId(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(RequestIdOptionKey, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return true;
        }

        foreach (var header in _requestIdHeaders)
        {
            if (request.Headers.TryGetValues(header, out var values) && values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                return true;
            }
        }

        if (request.Content != null)
        {
            foreach (var header in _requestIdHeaders)
            {
                if (request.Content.Headers.TryGetValues(header, out var values) && values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    return true;
                }
            }
        }

        var query = request.RequestUri?.Query;
        if (!string.IsNullOrEmpty(query))
        {
            if (ContainsQueryParameter(query, "requestId") ||
                ContainsQueryParameter(query, "clientRequestId"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the request carries an ETag or conditional match header (If-Match / If-None-Match / ETag).
    /// </summary>
    public static bool HasETag(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(ETagOptionKey, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return true;
        }

        foreach (var header in _etagHeaders)
        {
            if (request.Headers.TryGetValues(header, out var values) && values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsQueryParameter(string queryString, string parameterName)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return false;
        }

        var span = queryString.AsSpan();
        if (span.StartsWith("?"))
        {
            span = span[1..];
        }

        while (!span.IsEmpty)
        {
            var ampIndex = span.IndexOf('&');
            var part = ampIndex >= 0 ? span[..ampIndex] : span;
            span = ampIndex >= 0 ? span[(ampIndex + 1)..] : ReadOnlySpan<char>.Empty;

            var eqIndex = part.IndexOf('=');
            var key = eqIndex >= 0 ? part[..eqIndex] : part;
            if (key.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                if (eqIndex >= 0 && eqIndex < part.Length - 1)
                {
                    var val = part[(eqIndex + 1)..];
                    if (!val.IsEmpty)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Fluent helper to tag a request with a request ID for idempotency.
    /// </summary>
    public static HttpRequestMessage WithRequestId(this HttpRequestMessage request, string requestId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        request.Headers.TryAddWithoutValidation("X-Request-Id", requestId);
        request.Options.Set(RequestIdOptionKey, requestId);
        return request;
    }

    /// <summary>
    /// Fluent helper to tag a request with an ETag for conditional idempotency.
    /// </summary>
    public static HttpRequestMessage WithETag(this HttpRequestMessage request, string etag)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Options.Set(ETagOptionKey, etag);
        return request;
    }
}
