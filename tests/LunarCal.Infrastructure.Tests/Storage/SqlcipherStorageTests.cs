using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;

namespace LunarCal.Infrastructure.Tests.Storage;

public sealed class SqlcipherStorageTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _secretsDirectory;
    private readonly string _dbPath;

    public SqlcipherStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_StorageTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "test_lunarcal.db");

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
    public async Task Migrations_upgrade_from_empty_to_latest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var factory = new SqliteConnectionFactory(secretStore, options);

        // EnsureInitializedAsync runs all migrations from scratch
        await factory.EnsureInitializedAsync(ct);

        using var connection = await factory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);

        // Verify version in __schema_version table is 1
        var runner = new SqliteMigrationRunner();
        int version = await SqliteMigrationRunner.GetCurrentVersionAsync(connection, ct);
        version.Should().Be(1);

        // Verify all 9 tables from spec §6 are created
        var expectedTables = new[]
        {
            "Account",
            "CalendarInfo",
            "Event",
            "LunarRule",
            "PendingMutation",
            "AppSetting",
            "AiCommand",
            "ContributionOutbox",
            "ReminderState"
        };

        var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                actualTables.Add(reader.GetString(0));
            }
        }

        foreach (string table in expectedTables)
        {
            actualTables.Should().Contain(table);
        }

        // Verify index (calendar_id, start_utc) on Event table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Event' AND name = 'idx_event_calendar_start';";
            object? indexName = await cmd.ExecuteScalarAsync(ct);
            indexName.Should().NotBeNull();
            indexName!.ToString().Should().Be("idx_event_calendar_start");
        }

        // Running migrations again should be idempotent (0 newly applied)
        int reapplyCount = await runner.MigrateAsync(connection, ct);
        reapplyCount.Should().Be(0);
    }

    [Fact]
    public async Task SEC_DATA_1_Database_cannot_be_opened_without_key_or_with_wrong_key()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var factory = new SqliteConnectionFactory(secretStore, options);

        await factory.EnsureInitializedAsync(ct);

        // Clear pools to release locks on the file
        SqliteConnection.ClearAllPools();

        // 1. Attempt opening without password -> fails
        var noKeyConnStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath
        }.ToString();

        var actNoKey = () =>
        {
            using var conn = new SqliteConnection(noKeyConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Account;";
            cmd.ExecuteScalar();
        };
        actNoKey.Should().Throw<SqliteException>();

        // 2. Attempt opening with wrong password -> fails
        var wrongKeyConnStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Password = "WrongKey12345678901234567890123456789012345678901234567890123456"
        }.ToString();

        var actWrongKey = () =>
        {
            using var conn = new SqliteConnection(wrongKeyConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Account;";
            cmd.ExecuteScalar();
        };
        actWrongKey.Should().Throw<SqliteException>();
    }

    [Fact]
    public async Task SEC_DATA_1_Existing_database_with_missing_key_throws_InvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };

        // Initialize database first
        using (var factory = new SqliteConnectionFactory(secretStore, options))
        {
            await factory.EnsureInitializedAsync(ct);
        }

        SqliteConnection.ClearAllPools();

        // Delete the key from secret store while database file remains on disk
        await secretStore.DeleteSecretAsync(options.SecretKeyName, ct);

        // Re-creating the factory and initializing should securely fail
        using var factoryAfterLoss = new SqliteConnectionFactory(secretStore, options);
        var act = async () => await factoryAfterLoss.EnsureInitializedAsync(ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SEC_DATA_1_First_run_generates_256bit_key_and_persists_to_secret_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        using var factory = new SqliteConnectionFactory(secretStore, options);

        // Before init, key does not exist
        (await secretStore.HasSecretAsync(options.SecretKeyName, ct)).Should().BeFalse();

        await factory.EnsureInitializedAsync(ct);

        // After init, key exists
        (await secretStore.HasSecretAsync(options.SecretKeyName, ct)).Should().BeTrue();
        string? keyHex = await secretStore.GetSecretAsync(options.SecretKeyName, ct);

        // 256-bit key formatted in hex is exactly 64 hexadecimal characters
        keyHex.Should().NotBeNullOrWhiteSpace();
        keyHex!.Length.Should().Be(64);
    }

    [Fact]
    public async Task Migrations_incremental_upgrade_preserves_existing_data()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };

        // 1. Initialize V1 schema
        using (var factory = new SqliteConnectionFactory(secretStore, options))
        {
            await factory.EnsureInitializedAsync(ct);

            using var conn = await factory.CreateConnectionAsync(ct);
            await conn.OpenAsync(ct);

            // Insert sample account and event into V1
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Account (id, email, display_name) VALUES ('acc-1', 'test@example.com', 'Test User');
                INSERT INTO CalendarInfo (id, account_id, summary, color, selected) VALUES ('cal-1', 'acc-1', 'Work', '#2F6BFF', 1);
                INSERT INTO Event (id, calendar_id, title, start_utc, end_utc) VALUES ('evt-1', 'cal-1', 'Quarterly Meeting', '2026-11-02T10:00:00Z', '2026-11-02T11:00:00Z');
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        SqliteConnection.ClearAllPools();

        // 2. Define custom mock V2 migration
        var mockMigration2 = new MockMigration002();
        var customRunner = new SqliteMigrationRunner([new Migration001InitialSchema(), mockMigration2]);

        using (var factory = new SqliteConnectionFactory(secretStore, options, customRunner))
        {
            await factory.EnsureInitializedAsync(ct);

            using var conn = await factory.CreateConnectionAsync(ct);
            await conn.OpenAsync(ct);

            // Verify version is now 2
            int version = await SqliteMigrationRunner.GetCurrentVersionAsync(conn, ct);
            version.Should().Be(2);

            // Verify existing V1 data was preserved
            using var queryCmd = conn.CreateCommand();
            queryCmd.CommandText = "SELECT title FROM Event WHERE id = 'evt-1';";
            object? title = await queryCmd.ExecuteScalarAsync(ct);
            title?.ToString().Should().Be("Quarterly Meeting");

            // Verify V2 column was added
            using var checkV2Cmd = conn.CreateCommand();
            checkV2Cmd.CommandText = "SELECT new_v2_column FROM Account WHERE id = 'acc-1';";
            object? v2Val = await checkV2Cmd.ExecuteScalarAsync(ct);
            v2Val?.ToString().Should().Be("DefaultV2");
        }
    }

    private sealed class MockMigration002 : ISqliteMigration
    {
        public int Version => 2;
        public string Description => "Test incremental migration V2";

        public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken = default)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "ALTER TABLE Account ADD COLUMN new_v2_column TEXT DEFAULT 'DefaultV2';";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
