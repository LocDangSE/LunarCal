using System.Data.Common;
using System.Globalization;
using System.Text.Json;
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

public sealed class SyncEngineTests : IDisposable
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
    private readonly TestTimeProvider _timeProvider;
    private readonly ICalendarGateway _gateway = Substitute.For<ICalendarGateway>();
    private readonly IGoogleOAuthClient _oauthClient = Substitute.For<IGoogleOAuthClient>();

    public SyncEngineTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_SyncEngineTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "sync_test.db");

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_secretsDirectory);

        _secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        _connectionFactory = new SqliteConnectionFactory(_secretStore, options);

        _timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _connectionFactory.Dispose();
        _secretStore.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore transient test cleanup failure
            }
        }
    }

    [Fact]
    public async Task FR_SYNC_1_FR_SYNC_6_SyncInitialAsync_fetches_and_persists_calendars_events_and_extended_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        await _connectionFactory.EnsureInitializedAsync(ct);

        const string accountId = "google-user-123";
        const string accessToken = "valid-mock-access-token";

        // Setup OAuth token refresh
        _oauthClient.TryRefreshAccessTokenAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(GoogleTokenRefreshResult.Succeeded(new GoogleOAuthTokens
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ExpiresInSeconds = 3600
            }));

        // Setup Gateway: 2 calendars (1 selected, 1 unselected)
        var calPrimary = new CalendarInfo
        {
            Id = "primary-cal-id",
            AccountId = accountId,
            Summary = "Primary Work Calendar",
            Color = "#4285f4",
            IsPrimary = true,
            IsSelected = true
        };

        var calSecondary = new CalendarInfo
        {
            Id = "secondary-cal-id",
            AccountId = accountId,
            Summary = "Family Holidays",
            Color = "#0b8043",
            IsPrimary = false,
            IsSelected = false
        };

        _gateway.GetCalendarListAsync(accessToken, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CalendarListResult
            {
                Calendars = new[] { calPrimary, calSecondary }
            });

        // Setup Gateway: Events for selected calendar
        var timedEvent = new CalendarEvent
        {
            Id = "evt-timed-100",
            CalendarId = "primary-cal-id",
            GoogleId = "evt-timed-100",
            RecurringEventId = "master-series-001",
            Title = "Sprint Planning",
            Description = "Plan the sprint items",
            Location = "Meeting Room 1",
            StartUtc = new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero),
            AllDay = false,
            TimeZone = "Asia/Ho_Chi_Minh",
            Etag = "\"etag-timed-1\"",
            Updated = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            Status = "confirmed"
        };

        var allDayEvent = new CalendarEvent
        {
            Id = "evt-allday-200",
            CalendarId = "primary-cal-id",
            GoogleId = "evt-allday-200",
            RecurringEventId = null,
            Title = "Lunar Death Anniversary (Dam Gio)",
            Description = "Family memorial",
            Location = "Home",
            StartUtc = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero),
            AllDay = true,
            TimeZone = null,
            Etag = "\"etag-allday-2\"",
            Updated = new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero),
            Status = "confirmed",
            ExtendedProperties = new Dictionary<string, string>
            {
                ["lunarRuleId"] = "rule-lunar-555",
                ["createdBy"] = "lunarcal-chat",
                ["batchId"] = "batch-888"
            }
        };

        _gateway.GetEventsAsync(
            accessToken,
            calendarId: "primary-cal-id",
            singleEvents: true,
            timeMin: Arg.Any<DateTimeOffset?>(),
            timeMax: Arg.Any<DateTimeOffset?>(),
            pageToken: Arg.Any<string?>(),
            syncToken: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new EventsListResult
            {
                Events = new[] { timedEvent, allDayEvent },
                NextSyncToken = "sync-token-after-initial"
            });

        var syncEngine = new SyncEngine(_gateway, _oauthClient, _connectionFactory, _timeProvider);

        // Act
        var result = await syncEngine.SyncInitialAsync(accountId, ct);

        // Assert SyncResult
        result.Success.Should().BeTrue();
        result.CalendarsSynced.Should().Be(2);
        result.EventsSynced.Should().Be(2);

        // Verify SQLite database contents
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);

        // 1. Verify Account row created/ensured
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Account WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", accountId);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            count.Should().Be(1);
        }

        // 2. Verify CalendarInfo rows and sync_token
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, account_id, summary, color, selected, sync_token FROM CalendarInfo ORDER BY id";
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Row 1: primary calendar
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("primary-cal-id");
            reader.GetString(1).Should().Be(accountId);
            reader.GetString(2).Should().Be("Primary Work Calendar");
            reader.GetString(3).Should().Be("#4285f4");
            reader.GetInt32(4).Should().Be(1); // selected
            reader.GetString(5).Should().Be("sync-token-after-initial");

            // Row 2: secondary calendar (unselected, no events fetched)
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("secondary-cal-id");
            reader.GetString(1).Should().Be(accountId);
            reader.GetString(2).Should().Be("Family Holidays");
            reader.GetInt32(4).Should().Be(0); // not selected
            reader.IsDBNull(5).Should().BeTrue();

            (await reader.ReadAsync(ct)).Should().BeFalse();
        }

        // 3. Verify Event rows, recurring_event_id, etag, and extended_json
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id, calendar_id, google_id, recurring_event_id, title, description, location,
                       start_utc, end_utc, all_day, timezone, etag, updated, status, extended_json
                FROM Event
                ORDER BY id";

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Event 1: all-day event
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("evt-allday-200");
            reader.GetString(1).Should().Be("primary-cal-id");
            reader.GetString(2).Should().Be("evt-allday-200");
            reader.IsDBNull(3).Should().BeTrue(); // no recurringEventId
            reader.GetString(4).Should().Be("Lunar Death Anniversary (Dam Gio)");
            reader.GetInt32(9).Should().Be(1); // all_day == 1
            reader.GetString(11).Should().Be("\"etag-allday-2\"");

            var extendedJson = reader.GetString(14);
            extendedJson.Should().NotBeNullOrEmpty();
            var extProps = JsonSerializer.Deserialize<Dictionary<string, string>>(extendedJson);
            extProps.Should().NotBeNull();
            extProps!["lunarRuleId"].Should().Be("rule-lunar-555");
            extProps["createdBy"].Should().Be("lunarcal-chat");
            extProps["batchId"].Should().Be("batch-888");

            // Event 2: timed event
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("evt-timed-100");
            reader.GetString(1).Should().Be("primary-cal-id");
            reader.GetString(2).Should().Be("evt-timed-100");
            reader.GetString(3).Should().Be("master-series-001");
            reader.GetString(4).Should().Be("Sprint Planning");
            reader.GetInt32(9).Should().Be(0); // all_day == 0
            reader.GetString(10).Should().Be("Asia/Ho_Chi_Minh");
            reader.GetString(11).Should().Be("\"etag-timed-1\"");
            reader.IsDBNull(14).Should().BeTrue(); // no extended properties

            (await reader.ReadAsync(ct)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task FR_SYNC_1_Initial_sync_fails_when_reauthentication_required()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "expired-user";

        _oauthClient.TryRefreshAccessTokenAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(GoogleTokenRefreshResult.Failed(
                "invalid_grant",
                "Token expired or revoked",
                requiresReauthentication: true,
                guidanceMessage: "Token expired or revoked"));

        var syncEngine = new SyncEngine(_gateway, _oauthClient, _connectionFactory, _timeProvider);

        var result = await syncEngine.SyncInitialAsync(accountId, ct);

        result.Success.Should().BeFalse();
        result.RequiresReauthentication.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Token expired or revoked");
    }

    [Fact]
    public async Task FR_SYNC_1_Initial_sync_handles_sync_token_expired()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-sync-token-expired";
        const string accessToken = "valid-token";

        _oauthClient.TryRefreshAccessTokenAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(GoogleTokenRefreshResult.Succeeded(new GoogleOAuthTokens
            {
                AccessToken = accessToken,
                TokenType = "Bearer"
            }));

        _gateway.GetCalendarListAsync(accessToken, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GoogleCalendarSyncTokenExpiredException("primary"));

        var syncEngine = new SyncEngine(_gateway, _oauthClient, _connectionFactory, _timeProvider);

        var result = await syncEngine.SyncInitialAsync(accountId, ct);

        result.Success.Should().BeFalse();
        result.NeedsFullResync.Should().BeTrue();
    }

    [Fact]
    public async Task FR_SYNC_1_Initial_sync_validates_accountId_not_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = new SyncEngine(_gateway, _oauthClient, _connectionFactory, _timeProvider);

        var act = () => syncEngine.SyncInitialAsync("", ct);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
