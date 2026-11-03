using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Storage;

public sealed class InMemoryEventStoreTests
{
    [Fact]
    public async Task FR_UI_1_Sample_data_pre_seeds_expected_calendars_and_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: true);

        var calendars = await store.GetCalendarsAsync(cancellationToken: ct);
        calendars.Should().NotBeEmpty();
        calendars.Should().Contain(c => c.Id == "cal-google-primary" && c.IsPrimary);
        calendars.Should().Contain(c => c.Id == "cal-google-work");
        calendars.Should().Contain(c => c.Id == "cal-lunar-local");
        calendars.Should().Contain(c => c.Id == "cal-ai-tasks");

        // Query a wide interval covering sample events
        var now = DateTimeOffset.UtcNow;
        var events = await store.GetEventsAsync(now.AddDays(-30), now.AddDays(60), cancellationToken: ct);
        events.Should().NotBeEmpty();

        // Verify pre-seeded Lunar and AI properties (FR-SYNC-6)
        events.Should().Contain(e => e.LunarRuleId != null);
        events.Should().Contain(e => e.CreatedBy == "lunarcal-chat" && e.BatchId != null);
    }

    [Fact]
    public async Task FR_UI_1_Date_range_query_returns_overlapping_events_and_excludes_outside()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);

        var cal = new CalendarInfo
        {
            Id = "cal-test",
            AccountId = "acc-1",
            Summary = "Test Cal",
        };
        await store.UpsertCalendarAsync(cal, ct);

        var baseTime = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

        // Event A: 10:00 - 11:00 (inside)
        var evtA = CreateEvent("evt-a", baseTime, baseTime.AddHours(1));
        // Event B: 11:00 - 12:00 (inside)
        var evtB = CreateEvent("evt-b", baseTime.AddHours(1), baseTime.AddHours(2));
        // Event D: 09:00 - 10:00 (ends exactly at query start -> no overlap)
        var evtD = CreateEvent("evt-d", baseTime.AddHours(-1), baseTime);
        // Event E: 12:00 - 13:00 (starts exactly at query end -> no overlap)
        var evtE = CreateEvent("evt-e", baseTime.AddHours(2), baseTime.AddHours(3));
        // Event F: 09:30 - 10:30 (starts before, ends inside -> overlaps)
        var evtF = CreateEvent("evt-f", baseTime.AddMinutes(-30), baseTime.AddMinutes(30));
        // Event G: 11:30 - 12:30 (starts inside, ends after -> overlaps)
        var evtG = CreateEvent("evt-g", baseTime.AddMinutes(90), baseTime.AddMinutes(150));
        // Event H: 09:00 - 13:00 (encompasses interval -> overlaps)
        var evtH = CreateEvent("evt-h", baseTime.AddHours(-1), baseTime.AddHours(3));

        foreach (var ev in new[] { evtA, evtB, evtD, evtE, evtF, evtG, evtH })
        {
            await store.UpsertEventAsync(ev, ct);
        }

        // Query interval: [10:00, 12:00)
        var queryStart = baseTime;
        var queryEnd = baseTime.AddHours(2);
        var results = await store.GetEventsAsync(queryStart, queryEnd, cancellationToken: ct);

        var resultIds = results.Select(r => r.Id).ToList();

        // Overlapping events
        resultIds.Should().Contain("evt-a");
        resultIds.Should().Contain("evt-b");
        resultIds.Should().Contain("evt-f");
        resultIds.Should().Contain("evt-g");
        resultIds.Should().Contain("evt-h");

        // Non-overlapping events
        resultIds.Should().NotContain("evt-d");
        resultIds.Should().NotContain("evt-e");
    }

    [Fact]
    public async Task FR_UI_1_Date_range_query_with_calendar_id_filters_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);

        var time = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var evt1 = CreateEvent("evt-1", time, time.AddHours(1), "cal-1");
        var evt2 = CreateEvent("evt-2", time, time.AddHours(1), "cal-2");

        await store.UpsertEventAsync(evt1, ct);
        await store.UpsertEventAsync(evt2, ct);

        var cal1Events = await store.GetEventsAsync(time.AddHours(-1), time.AddHours(2), "cal-1", ct);
        cal1Events.Should().ContainSingle(e => e.Id == "evt-1");

        var cal2Events = await store.GetEventsAsync(time.AddHours(-1), time.AddHours(2), "cal-2", ct);
        cal2Events.Should().ContainSingle(e => e.Id == "evt-2");
    }

    [Fact]
    public async Task FR_UI_1_Date_range_query_with_invalid_range_returns_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: true);

        var time = DateTimeOffset.UtcNow;
        var result = await store.GetEventsAsync(time, time.AddHours(-1), cancellationToken: ct);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_UI_1_Event_CRUD_operations_work_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);

        var start = DateTimeOffset.UtcNow;
        var evt = CreateEvent("evt-crud", start, start.AddHours(1));

        // Create
        await store.UpsertEventAsync(evt, ct);
        var fetched = await store.GetEventByIdAsync("evt-crud", ct);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Test Event evt-crud");

        // Update
        var updated = evt with { Title = "Updated Title" };
        await store.UpsertEventAsync(updated, ct);
        var fetchedUpdated = await store.GetEventByIdAsync("evt-crud", ct);
        fetchedUpdated!.Title.Should().Be("Updated Title");

        // Delete
        await store.DeleteEventAsync("evt-crud", ct);
        var fetchedDeleted = await store.GetEventByIdAsync("evt-crud", ct);
        fetchedDeleted.Should().BeNull();
    }

    [Fact]
    public async Task FR_AUTH_5_Calendar_filtering_by_account_id_and_cascade_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);

        var cal1 = new CalendarInfo { Id = "cal-acc-1", AccountId = "acc-google-1", Summary = "Lịch 1" };
        var cal2 = new CalendarInfo { Id = "cal-acc-2", AccountId = "acc-google-2", Summary = "Lịch 2" };

        await store.UpsertCalendarAsync(cal1, ct);
        await store.UpsertCalendarAsync(cal2, ct);

        var acc1Calendars = await store.GetCalendarsAsync("acc-google-1", ct);
        acc1Calendars.Should().ContainSingle(c => c.Id == "cal-acc-1");

        // Add an event to cal-acc-1
        var now = DateTimeOffset.UtcNow;
        var evt = CreateEvent("evt-cascade", now, now.AddHours(1), "cal-acc-1");
        await store.UpsertEventAsync(evt, ct);

        // Delete calendar cal-acc-1
        await store.DeleteCalendarAsync("cal-acc-1", ct);
        var calCheck = await store.GetCalendarByIdAsync("cal-acc-1", ct);
        calCheck.Should().BeNull();

        // Event should also be cascade deleted
        var eventCheck = await store.GetEventByIdAsync("evt-cascade", ct);
        eventCheck.Should().BeNull();
    }

    [Fact]
    public void FR_UI_1_DependencyInjection_registers_IEventStore()
    {
        var services = new ServiceCollection();
        var configuration = Substitute.For<IConfiguration>();

        services.AddLunarCalInfrastructure(configuration);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventStore));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<SqliteEventStore>("the UI reads the SQLCipher cache since P3-R1");
    }

    private static CalendarEvent CreateEvent(string id, DateTimeOffset start, DateTimeOffset end, string calendarId = "cal-test")
    {
        return new CalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            Title = $"Test Event {id}",
            StartUtc = start,
            EndUtc = end,
        };
    }
}
