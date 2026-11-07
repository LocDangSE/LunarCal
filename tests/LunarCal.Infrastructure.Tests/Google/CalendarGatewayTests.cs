using System.Globalization;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class CalendarGatewayTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }

    [Fact]
    public async Task FR_SYNC_1_GetCalendarListAsync_success_parses_items_and_tokens()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            kind = "calendar#calendarList",
            etag = "\"list-etag-123\"",
            nextPageToken = "page-2",
            nextSyncToken = "sync-token-cal-list-001",
            items = new object[]
            {
                new
                {
                    id = "primary",
                    summary = "User Primary Calendar",
                    description = "Personal events",
                    primary = true,
                    selected = true,
                    accessRole = "owner",
                    backgroundColor = "#039be5",
                    foregroundColor = "#ffffff",
                    timeZone = "Asia/Ho_Chi_Minh"
                },
                new
                {
                    id = "holiday@group.calendar.google.com",
                    summary = "Holidays in Vietnam",
                    description = "Public holidays",
                    primary = false,
                    selected = false,
                    accessRole = "reader",
                    backgroundColor = "#0b8043",
                    timeZone = "Asia/Ho_Chi_Minh"
                }
            }
        });

        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var result = await gateway.GetCalendarListAsync("test-access-token-xyz", cancellationToken: ct);

        // Verify request details
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization!.Parameter.Should().Be("test-access-token-xyz");

        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/calendar/v3/users/me/calendarList");
        capturedRequest.RequestUri.Query.Should().Contain("minAccessRole=reader");
        capturedRequest.RequestUri.Query.Should().Contain("showHidden=false");

        // Verify result parsing
        result.Calendars.Should().HaveCount(2);
        result.NextPageToken.Should().Be("page-2");
        result.NextSyncToken.Should().Be("sync-token-cal-list-001");
        result.Etag.Should().Be("\"list-etag-123\"");

        var primary = result.Calendars[0];
        primary.Id.Should().Be("primary");
        primary.Summary.Should().Be("User Primary Calendar");
        primary.Color.Should().Be("#039be5");
        primary.IsPrimary.Should().BeTrue();
        primary.IsSelected.Should().BeTrue();
        primary.IsReadOnly.Should().BeFalse();
        primary.TimeZone.Should().Be("Asia/Ho_Chi_Minh");

        var secondary = result.Calendars[1];
        secondary.Id.Should().Be("holiday@group.calendar.google.com");
        secondary.Summary.Should().Be("Holidays in Vietnam");
        secondary.IsPrimary.Should().BeFalse();
        secondary.IsSelected.Should().BeFalse();
        secondary.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task FR_SYNC_1_GetCalendarListAsync_includes_pageToken_and_syncToken_when_provided()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            kind = "calendar#calendarList",
            items = Array.Empty<object>()
        });

        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        await gateway.GetCalendarListAsync("token", pageToken: "pt-123", syncToken: "st-456", cancellationToken: ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("pageToken=pt-123");
        capturedRequest.RequestUri.Query.Should().Contain("syncToken=st-456");
    }

    [Fact]
    public async Task FR_SYNC_1_GetCalendarListAsync_throws_GoogleCalendarSyncTokenExpiredException_on_410_Gone()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone));
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var act = () => gateway.GetCalendarListAsync("token", syncToken: "expired-sync-token", cancellationToken: ct);

        await act.Should().ThrowAsync<GoogleCalendarSyncTokenExpiredException>();
    }

    [Fact]
    public async Task FR_SYNC_1_GetCalendarListAsync_throws_HttpRequestException_on_http_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"invalid_token\"}", System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var act = () => gateway.GetCalendarListAsync("token", cancellationToken: ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FR_SYNC_1_FR_SYNC_6_GetEventsAsync_success_parses_timed_and_all_day_events_and_extended_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            kind = "calendar#events",
            etag = "\"events-etag-456\"",
            nextPageToken = "next-page-events",
            nextSyncToken = "next-sync-events-888",
            items = new object[]
            {
                // 1. Timed event with extendedProperties.private
                new
                {
                    id = "event-timed-001",
                    status = "confirmed",
                    summary = "Project Standup Meeting",
                    description = "Sprint sync with team",
                    location = "Room 402",
                    etag = "\"evt-etag-1\"",
                    recurringEventId = "recurring-master-99",
                    updated = "2026-11-03T08:30:00.000Z",
                    start = new
                    {
                        dateTime = "2026-11-03T09:00:00+07:00",
                        timeZone = "Asia/Ho_Chi_Minh"
                    },
                    end = new
                    {
                        dateTime = "2026-11-03T10:00:00+07:00",
                        timeZone = "Asia/Ho_Chi_Minh"
                    },
                    extendedProperties = new
                    {
                        @private = new Dictionary<string, string>
                        {
                            ["lunarRuleId"] = "rule-lunar-abc",
                            ["createdBy"] = "lunarcal-chat",
                            ["batchId"] = "batch-xyz-123"
                        }
                    }
                },
                // 2. All-day event
                new
                {
                    id = "event-allday-002",
                    status = "confirmed",
                    summary = "Lunar New Year Holiday",
                    etag = "\"evt-etag-2\"",
                    start = new
                    {
                        date = "2027-02-06"
                    },
                    end = new
                    {
                        date = "2027-02-07"
                    }
                }
            }
        });

        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var timeMin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeMax = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await gateway.GetEventsAsync(
            "test-access-token",
            calendarId: "primary",
            singleEvents: true,
            timeMin: timeMin,
            timeMax: timeMax,
            cancellationToken: ct);

        // Verify request query
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/calendar/v3/calendars/primary/events");
        capturedRequest.RequestUri.Query.Should().Contain("singleEvents=true");
        capturedRequest.RequestUri.Query.Should().Contain("timeMin=2026-01-01T00%3A00%3A00Z");
        capturedRequest.RequestUri.Query.Should().Contain("timeMax=2028-01-01T00%3A00%3A00Z");

        // Verify response result
        result.Events.Should().HaveCount(2);
        result.NextPageToken.Should().Be("next-page-events");
        result.NextSyncToken.Should().Be("next-sync-events-888");
        result.Etag.Should().Be("\"events-etag-456\"");

        // Check timed event
        var timed = result.Events[0];
        timed.Id.Should().Be("event-timed-001");
        timed.CalendarId.Should().Be("primary");
        timed.GoogleId.Should().Be("event-timed-001");
        timed.RecurringEventId.Should().Be("recurring-master-99");
        timed.Title.Should().Be("Project Standup Meeting");
        timed.Description.Should().Be("Sprint sync with team");
        timed.Location.Should().Be("Room 402");
        timed.AllDay.Should().BeFalse();
        timed.TimeZone.Should().Be("Asia/Ho_Chi_Minh");
        timed.Etag.Should().Be("\"evt-etag-1\"");
        timed.Status.Should().Be("confirmed");
        timed.StartUtc.Should().Be(DateTimeOffset.Parse("2026-11-03T02:00:00Z", CultureInfo.InvariantCulture));
        timed.EndUtc.Should().Be(DateTimeOffset.Parse("2026-11-03T03:00:00Z", CultureInfo.InvariantCulture));
        timed.Updated.Should().Be(DateTimeOffset.Parse("2026-11-03T08:30:00.000Z", CultureInfo.InvariantCulture));

        // Check extended properties
        timed.ExtendedProperties.Should().ContainKey("lunarRuleId");
        timed.LunarRuleId.Should().Be("rule-lunar-abc");
        timed.CreatedBy.Should().Be("lunarcal-chat");
        timed.BatchId.Should().Be("batch-xyz-123");

        // Check all-day event
        var allDay = result.Events[1];
        allDay.Id.Should().Be("event-allday-002");
        allDay.AllDay.Should().BeTrue();
        allDay.Title.Should().Be("Lunar New Year Holiday");
        allDay.StartUtc.Should().Be(new DateTimeOffset(2027, 2, 6, 0, 0, 0, TimeSpan.Zero));
        allDay.EndUtc.Should().Be(new DateTimeOffset(2027, 2, 7, 0, 0, 0, TimeSpan.Zero));
        allDay.LunarRuleId.Should().BeNull();
    }

    [Fact]
    public async Task FR_SYNC_1_GetEventsAsync_syncToken_omits_timeMin_and_timeMax()
    {
        var ct = TestContext.Current.CancellationToken;
        HttpRequestMessage? capturedRequest = null;

        var jsonResponse = JsonSerializer.Serialize(new
        {
            kind = "calendar#events",
            items = Array.Empty<object>()
        });

        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var timeMin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeMax = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // When syncToken is specified, timeMin and timeMax must be omitted
        await gateway.GetEventsAsync(
            "token",
            calendarId: "primary",
            singleEvents: true,
            timeMin: timeMin,
            timeMax: timeMax,
            syncToken: "sync-delta-token-123",
            cancellationToken: ct);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("syncToken=sync-delta-token-123");
        capturedRequest.RequestUri.Query.Should().NotContain("timeMin");
        capturedRequest.RequestUri.Query.Should().NotContain("timeMax");
    }

    [Fact]
    public async Task FR_SYNC_1_GetEventsAsync_throws_GoogleCalendarSyncTokenExpiredException_on_410_Gone()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeHandler = new FakeHttpMessageHandler(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone));
        });

        using var httpClient = new HttpClient(fakeHandler);
        var gateway = new CalendarGateway(httpClient);

        var act = () => gateway.GetEventsAsync(
            "token",
            calendarId: "primary",
            syncToken: "expired-sync-token",
            cancellationToken: ct);

        var ex = await act.Should().ThrowAsync<GoogleCalendarSyncTokenExpiredException>();
        ex.Which.CalendarId.Should().Be("primary");
    }

    [Fact]
    public async Task GetCalendarListAsync_validates_token_argument()
    {
        var ct = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var gateway = new CalendarGateway(httpClient);

        var act = () => gateway.GetCalendarListAsync("", cancellationToken: ct);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetEventsAsync_validates_arguments()
    {
        var ct = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var gateway = new CalendarGateway(httpClient);

        var act1 = () => gateway.GetEventsAsync("", "primary", cancellationToken: ct);
        await act1.Should().ThrowAsync<ArgumentException>();

        var act2 = () => gateway.GetEventsAsync("token", "", cancellationToken: ct);
        await act2.Should().ThrowAsync<ArgumentException>();
    }
}
