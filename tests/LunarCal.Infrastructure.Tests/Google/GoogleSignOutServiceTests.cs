using System.Globalization;
using System.Net;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Google.OAuth;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class GoogleSignOutServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _secretsDirectory;
    private readonly string _dbPath;

    public GoogleSignOutServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_SignOutTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "signout_test.db");

        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(_secretsDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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
    public async Task FR_AUTH_4_SignOut_sends_revoke_request_and_purges_account_data_from_db_and_secret_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var storageOptions = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var connectionFactory = new SqliteConnectionFactory(secretStore, storageOptions);

        await connectionFactory.EnsureInitializedAsync(ct);

        const string accountId1 = "account-user-001";
        const string accountId2 = "account-user-002";
        const string mockRefreshToken1 = "mock_refresh_token_to_revoke_111";

        // 1. Store refresh token in secret store
        await secretStore.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId1), mockRefreshToken1, ct);
        await secretStore.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId2), "mock_refresh_token_keep_222", ct);

        // 2. Seed database with account 1 and account 2 data
        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Account (id, email, display_name) VALUES
                    ('account-user-001', 'alice@example.com', 'Alice User'),
                    ('account-user-002', 'bob@example.com', 'Bob User');

                INSERT INTO CalendarInfo (id, account_id, summary) VALUES
                    ('cal-alice-work', 'account-user-001', 'Alice Work'),
                    ('cal-bob-personal', 'account-user-002', 'Bob Personal');

                INSERT INTO Event (id, calendar_id, title, start_utc, end_utc) VALUES
                    ('evt-alice-1', 'cal-alice-work', 'Alice Meeting', '2026-11-03T09:00:00Z', '2026-11-03T10:00:00Z'),
                    ('evt-bob-1', 'cal-bob-personal', 'Bob Meeting', '2026-11-03T14:00:00Z', '2026-11-03T15:00:00Z');

                INSERT INTO ReminderState (event_id, occurrence_start, snoozed_until) VALUES
                    ('evt-alice-1', '2026-11-03T09:00:00Z', '2026-11-03T08:50:00Z'),
                    ('evt-bob-1', '2026-11-03T14:00:00Z', '2026-11-03T13:50:00Z');

                INSERT INTO LunarRule (id, title, lunar_day, lunar_month, leap_policy, calendar_id, created_at) VALUES
                    ('rule-alice-1', 'Alice Anniversary', 15, 8, 'normal', 'cal-alice-work', '2026-11-01T00:00:00Z');
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3. Set up fake HTTP handler to capture the token revocation POST
        HttpRequestMessage? capturedRevokeRequest = null;
        string? capturedBody = null;

        var fakeHandler = new FakeHttpMessageHandler(async request =>
        {
            capturedRevokeRequest = request;
            if (request.Content is not null)
            {
                capturedBody = await request.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(fakeHandler);
        var oauthOptions = new GoogleOAuthOptions { ClientId = "test-client-id" };
        var oauthClient = new GoogleOAuthClient(oauthOptions, secretStore, httpClient: httpClient);

        var signOutService = new GoogleSignOutService(oauthClient, secretStore, connectionFactory);

        // 4. Execute sign-out with user confirmation (FR-AUTH-4)
        var result = await signOutService.SignOutAsync(accountId1, confirmed: true, ct);

        // 5. Verify sign-out result
        result.Success.Should().BeTrue();
        result.TokenRevokedOnServer.Should().BeTrue();
        result.CanceledOrUnconfirmed.Should().BeFalse();

        // FR-AUTH-4: Fake handler received revoke request with token parameter
        capturedRevokeRequest.Should().NotBeNull();
        capturedRevokeRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRevokeRequest.RequestUri.Should().Be(new Uri(oauthOptions.RevokeEndpoint));

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain($"token={mockRefreshToken1}");

        // SEC-OAUTH-3, FR-AUTH-4: Refresh token deleted from secret store
        var hasAccount1Token = await secretStore.HasSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId1), ct);
        hasAccount1Token.Should().BeFalse("account 1 refresh token must be deleted from secret store");

        var hasAccount2Token = await secretStore.HasSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId2), ct);
        hasAccount2Token.Should().BeTrue("account 2 refresh token must remain intact");

        // FR-AUTH-4: Account 1 data completely purged from SQLite database
        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);

            // Account 1 should have 0 records
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Account WHERE id = 'account-user-001'", ct)).Should().Be(0);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM CalendarInfo WHERE account_id = 'account-user-001'", ct)).Should().Be(0);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Event WHERE calendar_id = 'cal-alice-work'", ct)).Should().Be(0);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM ReminderState WHERE event_id = 'evt-alice-1'", ct)).Should().Be(0);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM LunarRule WHERE calendar_id = 'cal-alice-work'", ct)).Should().Be(0);

            // Account 2 should remain intact
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Account WHERE id = 'account-user-002'", ct)).Should().Be(1);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM CalendarInfo WHERE account_id = 'account-user-002'", ct)).Should().Be(1);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Event WHERE calendar_id = 'cal-bob-personal'", ct)).Should().Be(1);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM ReminderState WHERE event_id = 'evt-bob-1'", ct)).Should().Be(1);
        }
    }

    [Fact]
    public async Task FR_AUTH_4_SignOut_aborts_without_mutating_data_when_not_confirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var storageOptions = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var connectionFactory = new SqliteConnectionFactory(secretStore, storageOptions);

        await connectionFactory.EnsureInitializedAsync(ct);

        const string accountId = "account-unconfirmed";
        await secretStore.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), "mock_token_unconfirmed", ct);

        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Account (id, email) VALUES ('account-unconfirmed', 'unconfirmed@example.com');";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var oauthClient = Substitute.For<IGoogleOAuthClient>();
        var signOutService = new GoogleSignOutService(oauthClient, secretStore, connectionFactory);

        // Sign-out called with confirmed = false
        var result = await signOutService.SignOutAsync(accountId, confirmed: false, ct);

        result.Success.Should().BeFalse();
        result.CanceledOrUnconfirmed.Should().BeTrue();

        // OAuth client never invoked
        await oauthClient.DidNotReceiveWithAnyArgs().RevokeTokenAsync(default!, Arg.Any<CancellationToken>());

        // Token still in secret store
        (await secretStore.HasSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)).Should().BeTrue();

        // Database record still present
        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Account WHERE id = 'account-unconfirmed'", ct)).Should().Be(1);
        }
    }

    [Fact]
    public async Task FR_AUTH_4_SignOut_proceeds_with_local_purge_when_revoke_network_call_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var storageOptions = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var connectionFactory = new SqliteConnectionFactory(secretStore, storageOptions);

        await connectionFactory.EnsureInitializedAsync(ct);

        const string accountId = "account-offline";
        await secretStore.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), "mock_token_offline", ct);

        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Account (id, email) VALUES ('account-offline', 'offline@example.com');";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Simulate network failure when contacting Google's revoke endpoint
        var fakeHandler = new FakeHttpMessageHandler(_ =>
        {
            throw new HttpRequestException("Network is unreachable (offline)");
        });

        using var httpClient = new HttpClient(fakeHandler);
        var oauthOptions = new GoogleOAuthOptions { ClientId = "test-client-id" };
        var oauthClient = new GoogleOAuthClient(oauthOptions, secretStore, httpClient: httpClient);

        var signOutService = new GoogleSignOutService(oauthClient, secretStore, connectionFactory);

        // Sign-out should still succeed locally
        var result = await signOutService.SignOutAsync(accountId, confirmed: true, ct);

        result.Success.Should().BeTrue();
        result.TokenRevokedOnServer.Should().BeFalse();

        // Local token and database record are still deleted
        (await secretStore.HasSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)).Should().BeFalse();

        await using (var conn = await connectionFactory.CreateConnectionAsync(ct))
        {
            await conn.OpenAsync(ct);
            (await ExecuteScalarIntAsync(conn, "SELECT COUNT(*) FROM Account WHERE id = 'account-offline'", ct)).Should().Be(0);
        }
    }

    [Fact]
    public async Task FR_AUTH_4_RevokeTokenAsync_returns_true_on_200_and_on_400_bad_request()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);

        // Test 200 OK
        var handler200 = new FakeHttpMessageHandler(async _ =>
            await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using var client200 = new HttpClient(handler200);
        var oauthClient200 = new GoogleOAuthClient(new GoogleOAuthOptions { ClientId = "test-id" }, secretStore, httpClient: client200);
        var result200 = await oauthClient200.RevokeTokenAsync("mock_token_valid", ct);
        result200.Should().BeTrue();

        // Test 400 Bad Request (token already revoked/invalid)
        var handler400 = new FakeHttpMessageHandler(async _ =>
            await Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error": "invalid_token"}""", Encoding.UTF8, "application/json")
            }));

        using var client400 = new HttpClient(handler400);
        var oauthClient400 = new GoogleOAuthClient(new GoogleOAuthOptions { ClientId = "test-id" }, secretStore, httpClient: client400);
        var result400 = await oauthClient400.RevokeTokenAsync("mock_token_already_revoked", ct);
        result400.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_4_RevokeTokenAsync_returns_false_on_network_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);

        var handlerFail = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        using var clientFail = new HttpClient(handlerFail);
        var oauthClient = new GoogleOAuthClient(new GoogleOAuthOptions { ClientId = "test-id" }, secretStore, httpClient: clientFail);

        var result = await oauthClient.RevokeTokenAsync("mock_token_fail", ct);
        result.Should().BeFalse();
    }

    private static async Task<int> ExecuteScalarIntAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
