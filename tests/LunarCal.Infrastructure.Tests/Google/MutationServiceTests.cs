using System.Globalization;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Google.OAuth;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class MutationServiceTests : IDisposable
{
    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private readonly string _testDirectory;
    private readonly string _secretsDirectory;
    private readonly string _dbPath;
    private readonly DpapiSecretStore _secretStore;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePendingMutationStore _mutationStore;
    private readonly TestTimeProvider _timeProvider;
    private readonly ICalendarGateway _gateway = Substitute.For<ICalendarGateway>();
    private readonly IGoogleOAuthClient _oauthClient = Substitute.For<IGoogleOAuthClient>();
    private readonly ConflictNotifier _conflictNotifier = new();
    private readonly ISyncScheduler _syncScheduler = Substitute.For<ISyncScheduler>();
    private readonly MutationService _service;

    public MutationServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_MutationTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "mutation_test.db");

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_secretsDirectory);

        _secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        _connectionFactory = new SqliteConnectionFactory(_secretStore, options);

        _timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero));
        _mutationStore = new SqlitePendingMutationStore(_connectionFactory);

        _service = new MutationService(
            _connectionFactory,
            _mutationStore,
            _gateway,
            _oauthClient,
            _conflictNotifier,
            _syncScheduler,
            _timeProvider);

        // Default OAuth token setup
        _oauthClient.TryRefreshAccessTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GoogleTokenRefreshResult.Succeeded(
                new GoogleOAuthTokens
                {
                    AccessToken = "test-valid-access-token",
                    TokenType = "Bearer",
                    ExpiresInSeconds = 3600
                })));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private async Task SeedCalendarAndAccountAsync(string accountId, string calendarId)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Account (id, email, display_name) VALUES (@accId, 'user@gmail.com', 'User');
            INSERT INTO CalendarInfo (id, account_id, summary, color, selected) VALUES (@calId, @accId, 'Primary Cal', '#039be5', 1);";
        cmd.Parameters.AddWithValue("@accId", accountId);
        cmd.Parameters.AddWithValue("@calId", calendarId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    [Fact]
    public async Task FR_SYNC_4_CreateEventOptimisticAsync_persists_locally_and_enqueues_mutation()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var newEvent = new CalendarEvent
        {
            Id = "localevt001",
            CalendarId = "cal-1",
            Title = "Team Standup",
            StartUtc = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.Zero)
        };

        var created = await _service.CreateEventOptimisticAsync("acc-1", "cal-1", newEvent, ct);
        created.Id.Should().Be("localevt001");

        // Verify local Event table has the event
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT title FROM Event WHERE id = 'localevt001';";
        var titleInDb = await checkCmd.ExecuteScalarAsync(ct);
        titleInDb.Should().Be("Team Standup");

        // Verify PendingMutation table has the mutation
        var count = await _mutationStore.GetCountAsync(ct);
        count.Should().Be(1);

        var pending = await _mutationStore.GetPendingAsync(10, ct);
        pending[0].Kind.Should().Be(MutationKind.CreateEvent);
        pending[0].PayloadJson.Should().Contain("Team Standup");

        // Verify post mutation sync was requested
        await _syncScheduler.Received(1).TriggerPostMutationSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_SYNC_4_UpdateEventOptimisticAsync_persists_update_locally_and_enqueues_mutation()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var initialEvent = new CalendarEvent
        {
            Id = "evt-upd-1",
            CalendarId = "cal-1",
            Title = "Initial Title",
            Etag = "\"etag-initial\"",
            StartUtc = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero)
        };

        await _service.CreateEventOptimisticAsync("acc-1", "cal-1", initialEvent, ct);

        // Update event
        var updatedEvent = initialEvent with
        {
            Title = "Updated Title"
        };

        await _service.UpdateEventOptimisticAsync("acc-1", "cal-1", updatedEvent, ct);

        // Verify local Event table reflects the updated title
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT title FROM Event WHERE id = 'evt-upd-1';";
        var titleInDb = await checkCmd.ExecuteScalarAsync(ct);
        titleInDb.Should().Be("Updated Title");

        var pending = await _mutationStore.GetPendingAsync(10, ct);
        pending.Should().HaveCount(2); // Create + Update
        pending[1].Kind.Should().Be(MutationKind.UpdateEvent);
        var payload = System.Text.Json.JsonSerializer.Deserialize<EventMutationPayload>(pending[1].PayloadJson);
        payload.Should().NotBeNull();
        payload!.OriginalEtag.Should().Be("\"etag-initial\"");
    }

    [Fact]
    public async Task FR_SYNC_4_DeleteEventOptimisticAsync_removes_locally_and_enqueues_mutation()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var evt = new CalendarEvent
        {
            Id = "evt-del-1",
            CalendarId = "cal-1",
            Title = "To Delete",
            Etag = "\"etag-del-1\"",
            StartUtc = new DateTimeOffset(2026, 11, 4, 14, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero)
        };

        await _service.CreateEventOptimisticAsync("acc-1", "cal-1", evt, ct);
        await _service.DeleteEventOptimisticAsync("acc-1", "cal-1", "evt-del-1", "\"etag-del-1\"", ct);

        // Verify local Event table no longer has the event
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Event WHERE id = 'evt-del-1';";
        var countInDb = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        countInDb.Should().Be(0);

        var pending = await _mutationStore.GetPendingAsync(10, ct);
        pending[1].Kind.Should().Be(MutationKind.DeleteEvent);
        var payload = System.Text.Json.JsonSerializer.Deserialize<EventMutationPayload>(pending[1].PayloadJson);
        payload.Should().NotBeNull();
        payload!.OriginalEtag.Should().Be("\"etag-del-1\"");
    }

    [Fact]
    public async Task FR_SYNC_4_ProcessPendingMutationsAsync_when_offline_increments_attempts_and_preserves_local_data()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var newEvent = new CalendarEvent
        {
            Id = "evtoffline1",
            CalendarId = "cal-1",
            Title = "Offline Event",
            StartUtc = new DateTimeOffset(2026, 11, 4, 16, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 17, 0, 0, TimeSpan.Zero)
        };

        await _service.CreateEventOptimisticAsync("acc-1", "cal-1", newEvent, ct);

        // Configure gateway to fail with network error (offline)
        _gateway.CreateEventAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CalendarEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("No network connection available"));

        var result = await _service.ProcessPendingMutationsAsync("acc-1", ct);

        result.IsOffline.Should().BeTrue();
        result.FailedCount.Should().Be(1);
        result.SucceededCount.Should().Be(0);

        // Verify mutation is still in queue with attempts = 1
        var pending = await _mutationStore.GetPendingAsync(10, ct);
        pending.Should().HaveCount(1);
        pending[0].Attempts.Should().Be(1);
        pending[0].LastError.Should().Contain("No network connection available");

        // Verify local SQLite Event is still preserved for offline reading (NFR-REL)
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT title FROM Event WHERE id = 'evtoffline1';";
        var titleInDb = await checkCmd.ExecuteScalarAsync(ct);
        titleInDb.Should().Be("Offline Event");
    }

    [Fact]
    public async Task FR_SYNC_4_ProcessPendingMutationsAsync_when_online_dispatches_in_FIFO_order_and_updates_local_id_and_etag()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var newEvent = new CalendarEvent
        {
            Id = "localevt99",
            CalendarId = "cal-1",
            Title = "Created Online Event",
            StartUtc = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)
        };

        await _service.CreateEventOptimisticAsync("acc-1", "cal-1", newEvent, ct);

        _gateway.CreateEventAsync(Arg.Any<string>(), "cal-1", Arg.Any<CalendarEvent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(newEvent with
            {
                Id = "google-assigned-id-99",
                GoogleId = "google-assigned-id-99",
                Etag = "\"server-etag-v1\"",
                Updated = new DateTimeOffset(2026, 11, 3, 14, 5, 0, TimeSpan.Zero)
            }));

        var result = await _service.ProcessPendingMutationsAsync("acc-1", ct);

        result.IsOffline.Should().BeFalse();
        result.SucceededCount.Should().Be(1);
        result.FailedCount.Should().Be(0);

        // Queue should be cleared
        var pendingCount = await _mutationStore.GetCountAsync(ct);
        pendingCount.Should().Be(0);

        // Local SQLite event should be updated with Google ID and ETag
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT google_id, etag FROM Event WHERE id = 'localevt99';";
        using var reader = await checkCmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).Should().BeTrue();
        reader.GetString(0).Should().Be("google-assigned-id-99");
        reader.GetString(1).Should().Be("\"server-etag-v1\"");
    }

    [Fact]
    public async Task FR_SYNC_4_ProcessPendingMutationsAsync_HTTP_412_server_wins_and_notifies_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        var evt = new CalendarEvent
        {
            Id = "evt-conflict-1",
            CalendarId = "cal-1",
            GoogleId = "evt-conflict-1",
            Title = "Local Conflicting Title",
            Etag = "\"old-local-etag\"",
            StartUtc = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero)
        };

        // Seed initial event in SQLite
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Event (id, calendar_id, google_id, title, start_utc, end_utc, all_day, etag, status)
                VALUES ('evt-conflict-1', 'cal-1', 'evt-conflict-1', 'Initial Title', '2026-11-04T10:00:00Z', '2026-11-04T11:00:00Z', 0, '""old-local-etag""', 'confirmed');";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Trigger optimistic update
        await _service.UpdateEventOptimisticAsync("acc-1", "cal-1", evt, ct);

        // Gateway returns HTTP 412 on Update
        _gateway.UpdateEventAsync(Arg.Any<string>(), "cal-1", Arg.Any<CalendarEvent>(), "\"old-local-etag\"", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GooglePreconditionFailedException("cal-1", "evt-conflict-1", "\"old-local-etag\""));

        // Gateway returns server authoritative version on GetEventAsync
        _gateway.GetEventAsync(Arg.Any<string>(), "cal-1", "evt-conflict-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CalendarEvent?>(new CalendarEvent
            {
                Id = "evt-conflict-1",
                CalendarId = "cal-1",
                GoogleId = "evt-conflict-1",
                Title = "Server Winner Title",
                Etag = "\"server-winner-etag-v5\"",
                StartUtc = new DateTimeOffset(2026, 11, 4, 10, 30, 0, TimeSpan.Zero),
                EndUtc = new DateTimeOffset(2026, 11, 4, 11, 30, 0, TimeSpan.Zero),
                Status = "confirmed"
            }));

        EventConflictNotice? receivedNotice = null;
        _conflictNotifier.ConflictOccurred += (_, notice) => receivedNotice = notice;

        var result = await _service.ProcessPendingMutationsAsync("acc-1", ct);

        result.ConflictCount.Should().Be(1);
        result.SucceededCount.Should().Be(1); // Processed and resolved

        // Queue cleared of the conflicting mutation
        var pendingCount = await _mutationStore.GetCountAsync(ct);
        pendingCount.Should().Be(0);

        // Check local database: server version won!
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT title, etag FROM Event WHERE id = 'evt-conflict-1';";
            using var reader = await checkCmd.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("Server Winner Title");
            reader.GetString(1).Should().Be("\"server-winner-etag-v5\"");
        }

        // Verify user notification was raised (FR-SYNC-4)
        receivedNotice.Should().NotBeNull();
        receivedNotice!.EventId.Should().Be("evt-conflict-1");
        receivedNotice.CalendarId.Should().Be("cal-1");
        receivedNotice.AccountId.Should().Be("acc-1");
    }

    [Fact]
    public async Task FR_SYNC_4_ProcessPendingMutationsAsync_HTTP_412_on_delete_restores_server_event_and_notifies_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCalendarAndAccountAsync("acc-1", "cal-1");

        // Seed initial event in SQLite
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Event (id, calendar_id, google_id, title, start_utc, end_utc, all_day, etag, status)
                VALUES ('evt-del-412', 'cal-1', 'evt-del-412', 'Initial Delete Event', '2026-11-04T12:00:00Z', '2026-11-04T13:00:00Z', 0, '""old-del-etag""', 'confirmed');";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Optimistically delete event
        await _service.DeleteEventOptimisticAsync("acc-1", "cal-1", "evt-del-412", "\"old-del-etag\"", ct);

        // Gateway returns 412 on Delete
        _gateway.DeleteEventAsync(Arg.Any<string>(), "cal-1", "evt-del-412", "\"old-del-etag\"", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GooglePreconditionFailedException("cal-1", "evt-del-412", "\"old-del-etag\""));

        // Server still has the updated event
        _gateway.GetEventAsync(Arg.Any<string>(), "cal-1", "evt-del-412", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CalendarEvent?>(new CalendarEvent
            {
                Id = "evt-del-412",
                CalendarId = "cal-1",
                GoogleId = "evt-del-412",
                Title = "Restored Server Title",
                Etag = "\"server-updated-etag\"",
                StartUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero),
                EndUtc = new DateTimeOffset(2026, 11, 4, 13, 0, 0, TimeSpan.Zero),
                Status = "confirmed"
            }));

        EventConflictNotice? receivedNotice = null;
        _conflictNotifier.ConflictOccurred += (_, notice) => receivedNotice = notice;

        var result = await _service.ProcessPendingMutationsAsync("acc-1", ct);

        result.ConflictCount.Should().Be(1);

        // Local SQLite Event should have been restored because server won!
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT title FROM Event WHERE id = 'evt-del-412';";
            var titleInDb = await checkCmd.ExecuteScalarAsync(ct);
            titleInDb.Should().Be("Restored Server Title");
        }

        receivedNotice.Should().NotBeNull();
        receivedNotice!.EventId.Should().Be("evt-del-412");
    }
}
