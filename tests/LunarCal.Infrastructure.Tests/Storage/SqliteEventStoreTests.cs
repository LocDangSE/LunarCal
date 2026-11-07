using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Storage;

/// <summary>
/// P3-REVIEW (P3-R1): the calendar views read the SQLCipher cache the sync engine writes (FR-SYNC-1, FR-SYNC-6).
/// Before the fix the UI read an in-memory sample store and synced events never appeared.
/// </summary>
public sealed class SqliteEventStoreTests : IDisposable
{
    private static readonly DateTimeOffset _day = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"LunarCal_EventStore_{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Ignore transient cleanup failures.
        }
    }

    private async Task<SqliteConnectionFactory> CreateFactoryAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        var factory = new SqliteConnectionFactory(
            new DpapiSecretStore(Path.Combine(_dir, "secrets")),
            new SqliteStorageOptions { DatabasePath = Path.Combine(_dir, "cache.db") },
            new SqliteMigrationRunner());
        await factory.EnsureInitializedAsync(ct);
        await new SqliteAccountStore(factory).SaveAccountAsync(new AccountInfo { Id = "acc-1", Email = "me@gmail.com" }, ct);
        return factory;
    }

    private static CalendarEvent Event(string id, string calendarId, int startHour, int hours = 1, string? status = null) => new()
    {
        Id = id,
        CalendarId = calendarId,
        GoogleId = id,
        Title = $"Event {id}",
        StartUtc = _day.AddHours(startHour),
        EndUtc = _day.AddHours(startHour + hours),
        Status = status,
    };

    [Fact]
    public async Task FR_SYNC_1_Events_synced_from_Google_are_returned_by_the_store_the_views_read()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var gateway = Substitute.For<ICalendarGateway>();
        var oauth = Substitute.For<IGoogleOAuthClient>();
        oauth.TryRefreshAccessTokenAsync("acc-1", Arg.Any<CancellationToken>())
            .Returns(GoogleTokenRefreshResult.Succeeded(new GoogleOAuthTokens { AccessToken = "at", TokenType = "Bearer", ExpiresInSeconds = 3600 }));
        gateway.GetCalendarListAsync("at", null, null, Arg.Any<CancellationToken>()).Returns(new CalendarListResult
        {
            Calendars = [new CalendarInfo { Id = "me@gmail.com", AccountId = string.Empty, Summary = "Cá nhân", Color = "#2F6BFF" }],
        });
        gateway.GetEventsAsync("at", "me@gmail.com", true, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), null, null, Arg.Any<CancellationToken>())
            .Returns(new EventsListResult { Events = [Event("g1", "me@gmail.com", 9)], NextSyncToken = "sync-1" });

        (await new SyncEngine(gateway, oauth, factory).SyncInitialAsync("acc-1", ct)).Success.Should().BeTrue();

        var store = new SqliteEventStore(factory);
        var calendars = await store.GetCalendarsAsync(cancellationToken: ct);
        calendars.Should().ContainSingle();
        calendars[0].Summary.Should().Be("Cá nhân");
        calendars[0].IsPrimary.Should().BeTrue("the calendar id equals the account email");
        var events = await store.GetEventsAsync(_day, _day.AddDays(1), cancellationToken: ct);
        events.Select(e => e.Title).Should().Equal("Event g1");
        events[0].StartUtc.Should().Be(_day.AddHours(9));
    }

    [Fact]
    public async Task FR_UI_1_Range_query_returns_overlapping_events_of_selected_calendars_only()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteEventStore(factory);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "on", AccountId = "acc-1", Summary = "On" }, ct);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "off", AccountId = "acc-1", Summary = "Off", IsSelected = false }, ct);
        await store.UpsertEventAsync(Event("a", "on", 8), ct);
        await store.UpsertEventAsync(Event("overlap", "on", 23, hours: 2), ct); // crosses into the next day
        await store.UpsertEventAsync(Event("later", "on", 30), ct);
        await store.UpsertEventAsync(Event("hidden", "off", 9), ct);
        await store.UpsertEventAsync(Event("gone", "on", 10, status: "cancelled"), ct);

        var day = await store.GetEventsAsync(_day, _day.AddDays(1), cancellationToken: ct);
        day.Select(e => e.Id).Should().Equal("a", "overlap");

        var nextMorning = await store.GetEventsAsync(_day.AddDays(1), _day.AddDays(1).AddHours(1), cancellationToken: ct);
        nextMorning.Select(e => e.Id).Should().Equal("overlap");

        // An explicit calendar filter still returns a hidden calendar's events (e.g. for the editor).
        (await store.GetEventsAsync(_day, _day.AddDays(1), "off", ct)).Select(e => e.Id).Should().Equal("hidden");
    }

    [Fact]
    public async Task FR_UI_4_Event_round_trips_reminders_recurrence_and_extended_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteEventStore(factory);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "cal", AccountId = "acc-1", Summary = "Cal", IsReadOnly = true, TimeZone = "Asia/Ho_Chi_Minh" }, ct);
        await store.UpsertEventAsync(Event("r1", "cal", 9) with
        {
            ReminderMinutes = [10, 1440],
            Recurrence = ["RRULE:FREQ=YEARLY"],
            ExtendedProperties = new Dictionary<string, string> { ["batchId"] = "b-1" },
            AllDay = false,
            TimeZone = "Asia/Ho_Chi_Minh",
        }, ct);

        var loaded = await store.GetEventByIdAsync("r1", ct);
        loaded!.ReminderMinutes.Should().Equal(10, 1440);
        loaded.Recurrence.Should().Equal("RRULE:FREQ=YEARLY");
        loaded.BatchId.Should().Be("b-1");
        var calendar = await store.GetCalendarByIdAsync("cal", ct);
        calendar!.IsReadOnly.Should().BeTrue();
        calendar.TimeZone.Should().Be("Asia/Ho_Chi_Minh");

        await store.DeleteEventAsync("r1", ct);
        (await store.GetEventByIdAsync("r1", ct)).Should().BeNull();
    }

    [Fact]
    public async Task FR_UI_1_Sync_does_not_undo_the_sidebar_calendar_toggle()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteEventStore(factory);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "cal", AccountId = "acc-1", Summary = "Work", SyncToken = "s1" }, ct);
        await store.UpsertCalendarAsync((await store.GetCalendarByIdAsync("cal", ct))! with { IsSelected = false }, ct);

        var gateway = Substitute.For<ICalendarGateway>();
        var oauth = Substitute.For<IGoogleOAuthClient>();
        oauth.TryRefreshAccessTokenAsync("acc-1", Arg.Any<CancellationToken>())
            .Returns(GoogleTokenRefreshResult.Succeeded(new GoogleOAuthTokens { AccessToken = "at", TokenType = "Bearer", ExpiresInSeconds = 3600 }));
        gateway.GetCalendarListAsync("at", null, null, Arg.Any<CancellationToken>()).Returns(new CalendarListResult
        {
            Calendars = [new CalendarInfo { Id = "cal", AccountId = string.Empty, Summary = "Work (renamed)", IsSelected = true }],
        });
        gateway.GetEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EventsListResult { Events = [] });

        await new SyncEngine(gateway, oauth, factory).SyncInitialAsync("acc-1", ct);

        var calendar = await store.GetCalendarByIdAsync("cal", ct);
        calendar!.Summary.Should().Be("Work (renamed)");
        calendar.IsSelected.Should().BeFalse("the user switched it off locally");
    }

    [Fact]
    public void FR_SYNC_4_Event_ids_used_for_inserts_are_valid_Google_client_ids()
    {
        MutationService.IsValidGoogleEventId(MutationService.NewGoogleEventId()).Should().BeTrue();
        MutationService.IsValidGoogleEventId("local-evt-001").Should().BeFalse("'-' is not base32hex");
        MutationService.IsValidGoogleEventId("abcd").Should().BeFalse("ids need at least 5 characters");
        MutationService.IsValidGoogleEventId("0123456789abcdefghijklmnopqrstuv").Should().BeTrue();
        MutationService.IsValidGoogleEventId("wxyz1").Should().BeFalse("w-z are outside base32hex");
    }

    [Fact]
    public async Task FR_SYNC_1_GetEventById_and_DeleteEvent_with_calendar_id_isolates_events_sharing_the_same_id()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteEventStore(factory);

        await store.UpsertCalendarAsync(new CalendarInfo { Id = "cal-a", AccountId = "acc-1", Summary = "Calendar A" }, ct);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "cal-b", AccountId = "acc-1", Summary = "Calendar B" }, ct);

        var eventInA = Event("shared-id", "cal-a", 9) with { Title = "Event in Calendar A" };
        var eventInB = Event("shared-id", "cal-b", 14) with { Title = "Event in Calendar B" };

        await store.UpsertEventAsync(eventInA, ct);
        await store.UpsertEventAsync(eventInB, ct);

        // Fetch each independently using (calendarId, eventId)
        var fetchedA = await store.GetEventByIdAsync("cal-a", "shared-id", ct);
        var fetchedB = await store.GetEventByIdAsync("cal-b", "shared-id", ct);

        fetchedA.Should().NotBeNull();
        fetchedA!.Title.Should().Be("Event in Calendar A");

        fetchedB.Should().NotBeNull();
        fetchedB!.Title.Should().Be("Event in Calendar B");

        // Delete from Calendar A only
        await store.DeleteEventAsync("cal-a", "shared-id", ct);

        // Verification: Calendar A's event is gone, Calendar B's event remains intact
        (await store.GetEventByIdAsync("cal-a", "shared-id", ct)).Should().BeNull();
        var remainingB = await store.GetEventByIdAsync("cal-b", "shared-id", ct);
        remainingB.Should().NotBeNull();
        remainingB!.Title.Should().Be("Event in Calendar B");
    }
}
