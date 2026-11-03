using LunarCal.Infrastructure.Network;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Network;

public class HttpIdempotencyHelperTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void SEC_NET_2_Safe_methods_are_always_safe_to_retry(string method)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), "https://calendar.googleapis.com/calendar/v3/users/me/calendarList");

        var result = HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);

        Assert.True(result);
    }

    [Fact]
    public void SEC_NET_2_POST_without_requestId_or_ETag_is_not_retryable()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events");

        var result = HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);

        Assert.False(result);
    }

    [Theory]
    [InlineData("X-Request-Id")]
    [InlineData("Request-Id")]
    [InlineData("X-Client-Request-Id")]
    [InlineData("Client-Request-Id")]
    public void SEC_NET_2_POST_with_requestId_header_is_retryable(string headerName)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events");
        req.Headers.Add(headerName, "req-uuid-12345");

        var result = HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);

        Assert.True(result);
    }

    [Fact]
    public void SEC_NET_2_POST_with_query_requestId_is_retryable()
    {
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events?requestId=req-98765");
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events?foo=bar&clientRequestId=client-456");

        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req1));
        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req2));
    }

    [Theory]
    [InlineData("If-Match")]
    [InlineData("If-None-Match")]
    [InlineData("ETag")]
    public void SEC_NET_2_POST_with_ETag_header_is_retryable(string headerName)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events");
        req.Headers.Add(headerName, "\"etag-version-hash\"");

        var result = HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);

        Assert.True(result);
    }

    [Fact]
    public void SEC_NET_2_POST_with_fluent_extension_methods_is_retryable()
    {
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events")
            .WithRequestId("fluent-id-123");

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "https://calendar.googleapis.com/calendar/v3/calendars/primary/events")
            .WithETag("\"etag-789\"");

        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req1));
        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req2));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void SEC_NET_2_Write_methods_without_idempotency_key_are_not_retryable(string method)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), "https://calendar.googleapis.com/calendar/v3/calendars/primary/events/evt-123");

        var result = HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(req);

        Assert.False(result);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void SEC_NET_2_Write_methods_with_ETag_or_requestId_are_retryable(string method)
    {
        using var reqWithEtag = new HttpRequestMessage(new HttpMethod(method), "https://calendar.googleapis.com/calendar/v3/calendars/primary/events/evt-123")
            .WithETag("\"etag-val\"");

        using var reqWithId = new HttpRequestMessage(new HttpMethod(method), "https://calendar.googleapis.com/calendar/v3/calendars/primary/events/evt-123")
            .WithRequestId("req-val");

        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(reqWithEtag));
        Assert.True(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(reqWithId));
    }

    [Fact]
    public void SEC_NET_2_Null_request_returns_false()
    {
        Assert.False(HttpIdempotencyHelper.IsIdempotentOrSafeToRetry(null));
    }
}
