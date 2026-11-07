using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Google;

/// <summary>One Google account at a time (FR-AUTH-5, ADR-0029): real encrypted SQLite and DPAPI store, fake OAuth client.</summary>
public sealed class AccountSessionServiceTests : IDisposable
{
    private const string OldId = "old-account-1";
    private const string NewId = "new-account-2";

    private readonly string _directory;
    private readonly DpapiSecretStore _secrets;
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteAccountStore _accounts;
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly AccountSessionService _service;

    public AccountSessionServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"LunarCal_AccountSession_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _secrets = new DpapiSecretStore(Path.Combine(_directory, "secrets"));
        _connections = new SqliteConnectionFactory(_secrets, new SqliteStorageOptions { DatabasePath = Path.Combine(_directory, "app.db") });
        _accounts = new SqliteAccountStore(_connections);
        _oauth.RevokeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _service = new AccountSessionService(_accounts, _secrets, _oauth, new AccountDataPurger(_connections));
    }

    public void Dispose()
    {
        _secrets.Dispose();
        _connections.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // transient file lock on the temp folder
        }
    }

    [Fact]
    public async Task FR_AUTH_5_First_sign_in_saves_the_account_and_never_asks()
    {
        var ct = TestContext.Current.CancellationToken;
        await _connections.EnsureInitializedAsync(ct);
        var asked = false;

        var outcome = await _service.ApplySignInAsync(User(NewId, "new@example.com"), (_, _) => { asked = true; return Task.FromResult(true); }, ct);

        outcome.Should().Be(AccountSignInOutcome.Saved);
        asked.Should().BeFalse();
        (await _accounts.GetAllAccountsAsync(ct)).Select(a => a.Id).Should().Equal(NewId);
    }

    [Fact]
    public async Task FR_AUTH_5_Signing_in_again_with_the_same_account_keeps_every_row_and_never_asks()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        var asked = false;

        var outcome = await _service.ApplySignInAsync(User(OldId, "old@example.com"), (_, _) => { asked = true; return Task.FromResult(true); }, ct);

        outcome.Should().Be(AccountSignInOutcome.Saved);
        asked.Should().BeFalse();
        (await Count("PendingMutation", ct)).Should().Be(2, "a re-login after the token expired must keep the unsynced changes");
        (await Count("Event", ct)).Should().Be(1);
        (await Count("AiCommand", ct)).Should().Be(2);
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(OldId), ct)).Should().Be("old-refresh-token");
    }

    [Fact]
    public async Task FR_AUTH_5_A_different_account_is_compared_by_user_id_not_by_email()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        AccountSignInAssessment? seen = null;

        // Same e-mail, different Google user id: still a different account.
        await _service.ApplySignInAsync(User(NewId, "old@example.com"), (a, _) => { seen = a; return Task.FromResult(false); }, ct);

        seen.Should().NotBeNull();
        seen!.Kind.Should().Be(AccountSignInKind.DifferentAccount);
    }

    [Fact]
    public async Task FR_AUTH_5_The_confirmation_reports_what_would_be_lost()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        AccountSignInAssessment? seen = null;

        await _service.ApplySignInAsync(User(NewId, "new@example.com"), (a, _) => { seen = a; return Task.FromResult(false); }, ct);

        seen.Should().NotBeNull();
        seen!.CurrentAccount!.Id.Should().Be(OldId);
        seen.IncomingAccount.Id.Should().Be(NewId);
        seen.PendingChanges.Should().Be(2);
        seen.PendingAiCommands.Should().Be(1, "the completed command is history, not pending work");
        seen.LocalLunarRules.Should().Be(1);
        seen.HasDataToLose.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_5_Confirmed_switch_deletes_every_table_row_and_the_old_token_but_keeps_the_new_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);

        var outcome = await _service.ApplySignInAsync(User(NewId, "new@example.com"), (_, _) => Task.FromResult(true), ct);

        outcome.Should().Be(AccountSignInOutcome.Switched);
        foreach (var table in new[] { "Event", "CalendarInfo", "ReminderState", "LunarRule", "PendingMutation", "AiCommand" })
        {
            (await Count(table, ct)).Should().Be(0, $"{table} must not keep rows of the old account");
        }

        (await _accounts.GetAllAccountsAsync(ct)).Select(a => a.Id).Should().Equal(NewId);
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(OldId), ct)).Should().BeNull();
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(NewId), ct)).Should().Be("new-refresh-token");
        await _oauth.Received(1).RevokeTokenAsync("old-refresh-token", Arg.Any<CancellationToken>());
        await _oauth.DidNotReceive().RevokeTokenAsync("new-refresh-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Declining_the_switch_changes_nothing_and_discards_the_new_token()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);

        var outcome = await _service.ApplySignInAsync(User(NewId, "new@example.com"), (_, _) => Task.FromResult(false), ct);

        outcome.Should().Be(AccountSignInOutcome.Cancelled);
        (await Count("Event", ct)).Should().Be(1);
        (await Count("PendingMutation", ct)).Should().Be(2);
        (await Count("AiCommand", ct)).Should().Be(2);
        (await Count("LunarRule", ct)).Should().Be(1);
        (await _accounts.GetAllAccountsAsync(ct)).Select(a => a.Id).Should().Equal(OldId);
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(OldId), ct)).Should().Be("old-refresh-token");
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(NewId), ct)).Should().BeNull("the OAuth flow stored it before we knew the user declined");
        await _oauth.Received(1).RevokeTokenAsync("new-refresh-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Switching_still_deletes_the_tokens_when_revoking_fails_offline()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        _oauth.RevokeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new HttpRequestException("offline"));

        var outcome = await _service.ApplySignInAsync(User(NewId, "new@example.com"), (_, _) => Task.FromResult(true), ct);

        outcome.Should().Be(AccountSignInOutcome.Switched);
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(OldId), ct)).Should().BeNull("SEC-OAUTH-3: the local token goes even when Google cannot be reached");
        (await Count("PendingMutation", ct)).Should().Be(0);
    }

    [Fact]
    public async Task FR_AUTH_5_Leftover_second_account_from_before_ADR_0029_is_removed_too()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        await _accounts.SaveAccountAsync(new LunarCal.Core.Calendar.AccountInfo { Id = "legacy-3", Email = "legacy@example.com" }, ct);
        await _secrets.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken("legacy-3"), "legacy-refresh-token", ct);

        var outcome = await _service.ApplySignInAsync(User(NewId, "new@example.com"), (_, _) => Task.FromResult(true), ct);

        outcome.Should().Be(AccountSignInOutcome.Switched);
        (await _accounts.GetAllAccountsAsync(ct)).Select(a => a.Id).Should().Equal(NewId);
        (await _secrets.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken("legacy-3"), ct)).Should().BeNull();
    }

    [Fact]
    public async Task FR_AUTH_4_Sign_out_deletes_the_account_less_queues_but_keeps_other_accounts_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedOldAccountAsync(ct);
        await using (var connection = await _connections.CreateConnectionAsync(ct))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Account (id, email) VALUES ('other-9', 'other@example.com');
                INSERT INTO CalendarInfo (id, account_id, summary) VALUES ('cal-other', 'other-9', 'Other');
                INSERT INTO Event (id, calendar_id, title, start_utc, end_utc) VALUES ('evt-other', 'cal-other', 'Other', '2026-11-03T09:00:00Z', '2026-11-03T10:00:00Z');
                """;
            await command.ExecuteNonQueryAsync(ct);
        }

        await new AccountDataPurger(_connections).PurgeAccountAsync(OldId, ct);

        (await Count("PendingMutation", ct)).Should().Be(0, "a signed-out account's queued changes must never be replayed against the next account");
        (await Count("AiCommand", ct)).Should().Be(0);
        (await Count("LunarRule", ct)).Should().Be(0);
        (await Count("Event", ct)).Should().Be(1, "only the signed-out account's events go");
    }

    private static GoogleUserInfo User(string id, string email) => new() { Id = id, Email = email, DisplayName = "Test User" };

    private async Task<int> Count(string table, CancellationToken ct)
    {
        await using var connection = await _connections.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The stored account with data in every table the switch must clear, and both refresh tokens in the secret store.</summary>
    private async Task SeedOldAccountAsync(CancellationToken ct)
    {
        await _connections.EnsureInitializedAsync(ct);
        await _secrets.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(OldId), "old-refresh-token", ct);
        await _secrets.SetSecretAsync(KnownSecretKeys.GoogleRefreshToken(NewId), "new-refresh-token", ct);
        await using var connection = await _connections.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Account (id, email, display_name) VALUES ('old-account-1', 'old@example.com', 'Old User');
            INSERT INTO CalendarInfo (id, account_id, summary) VALUES ('cal-old', 'old-account-1', 'Old');
            INSERT INTO Event (id, calendar_id, title, start_utc, end_utc) VALUES ('evt-old', 'cal-old', 'Old event', '2026-11-03T09:00:00Z', '2026-11-03T10:00:00Z');
            INSERT INTO ReminderState (event_id, occurrence_start, snoozed_until) VALUES ('evt-old', '2026-11-03T09:00:00Z', '2026-11-03T08:50:00Z');
            INSERT INTO LunarRule (id, title, lunar_day, lunar_month, leap_policy, calendar_id, created_at) VALUES ('rule-old', 'Gio', 15, 8, 'normal', 'cal-old', '2026-11-01T00:00:00Z');
            INSERT INTO PendingMutation (id, kind, payload_json, created_at) VALUES ('m1', 'CreateEvent', '{}', '2026-11-03T08:00:00Z'), ('m2', 'DeleteEvent', '{}', '2026-11-03T08:01:00Z');
            INSERT INTO AiCommand (id, display_no, text, typed_at, timezone, input_lang, status, created_at, updated_at)
                VALUES ('c1', 1000, 'pending command', '2026-11-03T08:00:00Z', 'Asia/Ho_Chi_Minh', 'vi', 'pending', '2026-11-03T08:00:00Z', '2026-11-03T08:00:00Z'),
                       ('c2', 1001, 'finished command', '2026-11-03T08:00:00Z', 'Asia/Ho_Chi_Minh', 'vi', 'completed', '2026-11-03T08:00:00Z', '2026-11-03T08:00:00Z');
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}
