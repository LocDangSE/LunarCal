using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;

namespace LunarCal.Infrastructure.Tests.Google;

/// <summary>
/// P3-REVIEW (P3-R2) regression tests for the event body LunarCal sends to Google and reads back (FR-UI-4,
/// FR-SYNC-4, FR-SYNC-6).
/// </summary>
public sealed class CalendarGatewayEventBodyTests
{
    private static readonly string[] _dailyRule = ["RRULE:FREQ=DAILY;COUNT=3"];
    private static readonly TimeZoneInfo _vietnam = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    private static JsonElement Body(CalendarEvent evt, bool includeId = false)
    {
        using var content = CalendarGateway.BuildEventJsonContent(evt, includeId);
        var json = content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void FR_UI_4_All_day_event_created_at_UTC_plus_7_keeps_its_calendar_date()
    {
        // The editor stores an all-day event on 22/10/2026 as local midnight in the user's zone (= 21/10 17:00 UTC).
        var evt = new CalendarEvent
        {
            Id = "abc12345",
            CalendarId = "primary",
            Title = "Giỗ ông nội",
            AllDay = true,
            TimeZone = _vietnam.Id,
            StartUtc = CalendarTime.StartOfDayUtc(new DateOnly(2026, 10, 22), _vietnam),
            EndUtc = CalendarTime.StartOfDayUtc(new DateOnly(2026, 10, 23), _vietnam),
        };

        var body = Body(evt);

        body.GetProperty("start").GetProperty("date").GetString().Should().Be("2026-10-22");
        body.GetProperty("end").GetProperty("date").GetString().Should().Be("2026-10-23");
        body.GetProperty("start").TryGetProperty("dateTime", out _).Should().BeFalse();
    }

    [Fact]
    public void FR_UI_4_Timed_event_sends_IANA_time_zone_reminders_and_recurrence()
    {
        var evt = new CalendarEvent
        {
            Id = "abc12345",
            CalendarId = "primary",
            Title = "Họp nhóm",
            TimeZone = _vietnam.Id, // Windows id, as the editor writes it
            StartUtc = new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 5, 3, 0, 0, TimeSpan.Zero),
            ReminderMinutes = [1440, 10],
            Recurrence = ["RRULE:FREQ=WEEKLY;BYDAY=TH"],
        };

        var body = Body(evt, includeId: true);

        body.GetProperty("id").GetString().Should().Be("abc12345");
        body.GetProperty("start").GetProperty("dateTime").GetString().Should().Be("2026-11-05T02:00:00Z");
        body.GetProperty("start").GetProperty("timeZone").GetString().Should().Be("Asia/Bangkok");
        var reminders = body.GetProperty("reminders");
        reminders.GetProperty("useDefault").GetBoolean().Should().BeFalse();
        reminders.GetProperty("overrides").EnumerateArray()
            .Select(o => (o.GetProperty("method").GetString(), o.GetProperty("minutes").GetInt32()))
            .Should().Equal(("popup", 10), ("popup", 1440));
        body.GetProperty("recurrence").EnumerateArray().Select(e => e.GetString()).Should().Equal("RRULE:FREQ=WEEKLY;BYDAY=TH");
    }

    [Fact]
    public void FR_UI_4_Null_reminders_keep_calendar_defaults()
    {
        var evt = new CalendarEvent
        {
            Id = "abc12345",
            CalendarId = "primary",
            Title = "x",
            StartUtc = DateTimeOffset.UnixEpoch,
            EndUtc = DateTimeOffset.UnixEpoch.AddHours(1),
        };

        Body(evt).TryGetProperty("reminders", out _).Should().BeFalse();
    }

    [Fact]
    public async Task FR_SYNC_6_Parsed_event_reads_popup_reminders_recurrence_and_all_day_date()
    {
        var ct = TestContext.Current.CancellationToken;
        var json = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = "e1",
                    summary = "Cả ngày",
                    start = new { date = "2026-10-22" },
                    end = new { date = "2026-10-23" },
                    reminders = new { useDefault = false, overrides = new object[] { new { method = "email", minutes = 60 }, new { method = "popup", minutes = 30 } } },
                },
                new
                {
                    id = "e2",
                    summary = "Mặc định",
                    start = new { dateTime = "2026-10-22T02:00:00Z" },
                    end = new { dateTime = "2026-10-22T03:00:00Z" },
                    reminders = new { useDefault = true },
                    recurrence = _dailyRule,
                },
            },
        });
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        using var client = new HttpClient(handler);

        var result = await new CalendarGateway(client).GetEventsAsync("t", "primary", true, null, null, null, null, ct);

        var allDay = result.Events.Single(e => e.Id == "e1");
        allDay.AllDay.Should().BeTrue();
        allDay.TimeZone.Should().Be("UTC");
        CalendarTime.GetCoveredDates(allDay, _vietnam).Should().Be((new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22)));
        allDay.ReminderMinutes.Should().Equal(30);

        var timed = result.Events.Single(e => e.Id == "e2");
        timed.ReminderMinutes.Should().BeNull("useDefault means the calendar's default reminders");
        timed.Recurrence.Should().Equal("RRULE:FREQ=DAILY;COUNT=3");
    }

    [Fact]
    public async Task SEC_NET_2_Create_returning_409_for_existing_client_id_returns_the_stored_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var stored = JsonSerializer.Serialize(new
        {
            id = "abc12345",
            etag = "\"e1\"",
            summary = "Retried",
            start = new { dateTime = "2026-11-05T02:00:00Z" },
            end = new { dateTime = "2026-11-05T03:00:00Z" },
        });
        var calls = new List<HttpMethod>();
        using var handler = new StubHandler(req =>
        {
            calls.Add(req.Method);
            return req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stored) };
        });
        using var client = new HttpClient(handler);
        var evt = new CalendarEvent
        {
            Id = "abc12345",
            CalendarId = "primary",
            Title = "Retried",
            StartUtc = new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 5, 3, 0, 0, TimeSpan.Zero),
        };

        var created = await new CalendarGateway(client).CreateEventAsync("t", "primary", evt, "req-1", ct);

        created.Id.Should().Be("abc12345");
        created.Etag.Should().Be("\"e1\"");
        calls.Should().Equal(HttpMethod.Post, HttpMethod.Get);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
