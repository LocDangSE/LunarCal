using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class CalendarGatewayMutationTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }

    [Fact]
    public async Task FR_SYNC_4_CreateEventAsync_sends_post_with_x_request_id_and_parses_response()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            id = "created-remote-id-123",
            etag = "\"created-etag-v1\"",
            status = "confirmed",
            summary = "Dentist Appointment",
            start = new { dateTime = "2026-11-04T10:00:00Z" },
            end = new { dateTime = "2026-11-04T11:00:00Z" },
            updated = "2026-11-03T12:00:00Z",
            extendedProperties = new
            {
                @private = new Dictionary<string, string> { { "createdBy", "lunarcal-chat" } }
            }
        });

        using var handler = new FakeHttpMessageHandler(async req =>
        {
            capturedRequest = req;
            if (req.Content != null)
            {
                capturedBody = await req.Content.ReadAsStringAsync(ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        var newEvent = new CalendarEvent
        {
            Id = "temp-local-id",
            CalendarId = "primary",
            Title = "Dentist Appointment",
            StartUtc = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero),
            ExtendedProperties = new Dictionary<string, string> { { "createdBy", "lunarcal-chat" } }
        };

        var result = await gateway.CreateEventAsync("token-123", "primary", newEvent, "req-id-abc", ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().Be("https://www.googleapis.com/calendar/v3/calendars/primary/events");
        capturedRequest.Headers.Authorization!.Parameter.Should().Be("token-123");
        capturedRequest.Headers.GetValues("X-Request-Id").Should().Contain("req-id-abc");

        capturedBody.Should().Contain("Dentist Appointment");
        capturedBody.Should().Contain("lunarcal-chat");

        result.Id.Should().Be("created-remote-id-123");
        result.GoogleId.Should().Be("created-remote-id-123");
        result.Etag.Should().Be("\"created-etag-v1\"");
        result.Title.Should().Be("Dentist Appointment");
    }

    [Fact]
    public async Task FR_SYNC_4_UpdateEventAsync_sends_put_with_if_match_and_x_request_id()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            id = "google-evt-789",
            etag = "\"updated-etag-v2\"",
            status = "confirmed",
            summary = "Updated Meeting",
            start = new { dateTime = "2026-11-04T14:00:00Z" },
            end = new { dateTime = "2026-11-04T15:00:00Z" }
        });

        using var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            });
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        var updateEvent = new CalendarEvent
        {
            Id = "google-evt-789",
            CalendarId = "primary",
            GoogleId = "google-evt-789",
            Title = "Updated Meeting",
            StartUtc = new DateTimeOffset(2026, 11, 4, 14, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero)
        };

        var result = await gateway.UpdateEventAsync("token-123", "primary", updateEvent, "\"v1-etag\"", "client-req-999", ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Put);
        capturedRequest.RequestUri!.ToString().Should().Be("https://www.googleapis.com/calendar/v3/calendars/primary/events/google-evt-789");
        capturedRequest.Headers.GetValues("If-Match").Should().Contain("\"v1-etag\"");
        capturedRequest.Headers.GetValues("X-Request-Id").Should().Contain("client-req-999");

        result.Etag.Should().Be("\"updated-etag-v2\"");
        result.Title.Should().Be("Updated Meeting");
    }

    [Fact]
    public async Task FR_SYNC_4_UpdateEventAsync_throws_GooglePreconditionFailedException_on_HTTP_412()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new FakeHttpMessageHandler(_ =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
            {
                Content = new StringContent("{\"error\": {\"message\": \"Precondition Failed\"}}")
            });
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        var evt = new CalendarEvent
        {
            Id = "evt-conflict",
            CalendarId = "primary",
            Title = "Conflicted Title",
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        var act = async () => await gateway.UpdateEventAsync("token-123", "primary", evt, "\"old-etag\"", cancellationToken: ct);

        var ex = await Assert.ThrowsAsync<GooglePreconditionFailedException>(act);
        ex.CalendarId.Should().Be("primary");
        ex.EventId.Should().Be("evt-conflict");
        ex.ExpectedEtag.Should().Be("\"old-etag\"");
    }

    [Fact]
    public async Task FR_SYNC_4_DeleteEventAsync_sends_delete_with_if_match_and_x_request_id()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        using var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        await gateway.DeleteEventAsync("token-123", "primary", "evt-del-123", "\"etag-del\"", "req-del-1", ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Delete);
        capturedRequest.RequestUri!.ToString().Should().Be("https://www.googleapis.com/calendar/v3/calendars/primary/events/evt-del-123");
        capturedRequest.Headers.GetValues("If-Match").Should().Contain("\"etag-del\"");
        capturedRequest.Headers.GetValues("X-Request-Id").Should().Contain("req-del-1");
    }

    [Fact]
    public async Task FR_SYNC_4_DeleteEventAsync_throws_GooglePreconditionFailedException_on_HTTP_412()
    {
        var ct = TestContext.Current.CancellationToken;

        using var handler = new FakeHttpMessageHandler(_ =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed));
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        var act = async () => await gateway.DeleteEventAsync("token-123", "primary", "evt-del-conflict", "\"old-etag\"", cancellationToken: ct);

        var ex = await Assert.ThrowsAsync<GooglePreconditionFailedException>(act);
        ex.CalendarId.Should().Be("primary");
        ex.EventId.Should().Be("evt-del-conflict");
    }

    [Fact]
    public async Task FR_SYNC_4_GetEventAsync_returns_event_or_null_on_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            id = "google-evt-found",
            etag = "\"server-latest-etag\"",
            summary = "Server Authoritative Title",
            start = new { dateTime = "2026-11-04T12:00:00Z" },
            end = new { dateTime = "2026-11-04T13:00:00Z" }
        });

        using var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("google-evt-found"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jsonResponse)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var client = new HttpClient(handler);
        var gateway = new CalendarGateway(client);

        var found = await gateway.GetEventAsync("token-123", "primary", "google-evt-found", ct);
        found.Should().NotBeNull();
        found!.Title.Should().Be("Server Authoritative Title");
        found.Etag.Should().Be("\"server-latest-etag\"");

        var missing = await gateway.GetEventAsync("token-123", "primary", "missing-evt", ct);
        missing.Should().BeNull();
    }
}
