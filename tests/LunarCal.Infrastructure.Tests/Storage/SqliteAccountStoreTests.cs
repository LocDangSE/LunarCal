using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Storage;

/// <summary>
/// P3-REVIEW regression tests: <see cref="ISqliteConnectionFactory.CreateConnectionAsync"/> returns a closed
/// connection, so every consumer must open it. The account store and the scheduler's account lookup did not, which
/// made sign-in fail on save and left the scheduler with no account to sync (FR-AUTH-1, FR-AUTH-4, FR-SYNC-3).
/// </summary>
public sealed class SqliteAccountStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_AccountTest_{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Ignore transient cleanup failures.
        }
    }

    private async Task<SqliteConnectionFactory> CreateFactoryAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_testDirectory);
        var factory = new SqliteConnectionFactory(
            new DpapiSecretStore(Path.Combine(_testDirectory, "secrets")),
            new SqliteStorageOptions { DatabasePath = Path.Combine(_testDirectory, "accounts.db") },
            new SqliteMigrationRunner());
        await factory.EnsureInitializedAsync(ct);
        return factory;
    }

    [Fact]
    public async Task FR_AUTH_1_Saved_account_round_trips_through_sqlite_and_FR_AUTH_4_delete_removes_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteAccountStore(factory);

        await store.SaveAccountAsync(new AccountInfo { Id = "acc-1", Email = "user@gmail.com", DisplayName = "User One" }, ct);

        // A fresh store instance must read from the database, not from the in-memory fallback list.
        var reloaded = await new SqliteAccountStore(factory).GetPrimaryAccountAsync(ct);
        reloaded.Should().NotBeNull();
        reloaded!.Id.Should().Be("acc-1");
        reloaded.Email.Should().Be("user@gmail.com");
        reloaded.DisplayName.Should().Be("User One");
        (await store.GetAllAccountsAsync(ct)).Select(a => a.Id).Should().Equal("acc-1");

        await store.DeleteAccountAsync("acc-1", ct);
        (await new SqliteAccountStore(factory).GetPrimaryAccountAsync(ct)).Should().BeNull();
    }

    [Fact]
    public async Task FR_SYNC_3_Scheduler_syncs_accounts_stored_in_sqlite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        await new SqliteAccountStore(factory).SaveAccountAsync(new AccountInfo { Id = "acc-1", Email = "user@gmail.com" }, ct);

        var engine = Substitute.For<ISyncEngine>();
        engine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SyncResult.Succeeded(1, 0));
        var scheduler = new SyncScheduler(engine, connectionFactory: factory);

        await scheduler.TriggerManualSyncAsync(ct);

        await engine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_10_Scheduler_without_accounts_does_not_report_a_sync_start()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var engine = Substitute.For<ISyncEngine>();
        var scheduler = new SyncScheduler(engine, connectionFactory: factory);
        var started = false;
        scheduler.SyncStarted += (_, _) => started = true;

        await scheduler.TriggerManualSyncAsync(ct);

        started.Should().BeFalse("a start without a matching completion leaves the sidebar on 'Syncing...'");
        scheduler.IsSyncInProgress.Should().BeFalse();
        await engine.DidNotReceive().SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
