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

        // Verify the latest schema version (V3, P4-R3) is recorded in __schema_version
        int version = await SqliteMigrationRunner.GetCurrentVersionAsync(connection, ct);
        version.Should().Be(3);

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
        int reapplyCount = await new SqliteMigrationRunner().MigrateAsync(connection, ct);
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

        // 1. Initialize V1 schema only
        using (var factory = new SqliteConnectionFactory(secretStore, options, new SqliteMigrationRunner([new Migration001InitialSchema()])))
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

        // 2. Upgrade with the production runner (V1 + V2 + V3, P3-R1/P3-R2/P4-R3)
        using (var factory = new SqliteConnectionFactory(secretStore, options, new SqliteMigrationRunner()))
        {
            await factory.EnsureInitializedAsync(ct);

            using var conn = await factory.CreateConnectionAsync(ct);
            await conn.OpenAsync(ct);

            // Verify version is now 3
            int version = await SqliteMigrationRunner.GetCurrentVersionAsync(conn, ct);
            version.Should().Be(3);

            // Verify existing V1 data was preserved
            using var queryCmd = conn.CreateCommand();
            queryCmd.CommandText = "SELECT title FROM Event WHERE id = 'evt-1';";
            object? title = await queryCmd.ExecuteScalarAsync(ct);
            title?.ToString().Should().Be("Quarterly Meeting");

            // Verify the V2 columns exist and start empty for upgraded rows
            using var checkV2Cmd = conn.CreateCommand();
            checkV2Cmd.CommandText = "SELECT reminders_json IS NULL AND recurrence_json IS NULL FROM Event WHERE id = 'evt-1';";
            Convert.ToInt32(await checkV2Cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
            checkV2Cmd.CommandText = "SELECT access_role IS NULL AND time_zone IS NULL FROM CalendarInfo WHERE id = 'cal-1';";
            Convert.ToInt32(await checkV2Cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
        }
    }


    [Fact]
    public async Task FR_SYNC_1_Migration_V2_to_V3_preserves_event_data_and_enables_same_id_in_different_calendars()
    {
        var ct = TestContext.Current.CancellationToken;
        using var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };

        // 1. Initialize schema up to V2 only
        using (var factory = new SqliteConnectionFactory(
            secretStore,
            options,
            new SqliteMigrationRunner([new Migration001InitialSchema(), new Migration002EventDetails()])))
        {
            await factory.EnsureInitializedAsync(ct);

            using var conn = await factory.CreateConnectionAsync(ct);
            await conn.OpenAsync(ct);

            int v2 = await SqliteMigrationRunner.GetCurrentVersionAsync(conn, ct);
            v2.Should().Be(2);

            // Insert two calendars and an event into cal-1
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Account (id, email, display_name) VALUES ('acc-1', 'test@example.com', 'Test User');
                INSERT INTO CalendarInfo (id, account_id, summary, color, selected) VALUES ('cal-1', 'acc-1', 'Work', '#2F6BFF', 1);
                INSERT INTO CalendarInfo (id, account_id, summary, color, selected) VALUES ('cal-2', 'acc-1', 'Personal', '#12B76A', 1);
                INSERT INTO Event (id, calendar_id, title, start_utc, end_utc, recurrence_json, reminders_json)
                VALUES ('evt-shared', 'cal-1', 'Existing Event in Cal1', '2026-11-02T10:00:00Z', '2026-11-02T11:00:00Z', '["RRULE:FREQ=DAILY"]', '[10]');
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        SqliteConnection.ClearAllPools();

        // 2. Upgrade to latest (V3)
        using (var factory = new SqliteConnectionFactory(secretStore, options, new SqliteMigrationRunner()))
        {
            await factory.EnsureInitializedAsync(ct);

            using var conn = await factory.CreateConnectionAsync(ct);
            await conn.OpenAsync(ct);

            int v3 = await SqliteMigrationRunner.GetCurrentVersionAsync(conn, ct);
            v3.Should().Be(3);

            // Existing data is preserved
            using (var queryCmd = conn.CreateCommand())
            {
                queryCmd.CommandText = "SELECT title, recurrence_json, reminders_json FROM Event WHERE id = 'evt-shared' AND calendar_id = 'cal-1';";
                using var reader = await queryCmd.ExecuteReaderAsync(ct);
                (await reader.ReadAsync(ct)).Should().BeTrue();
                reader.GetString(0).Should().Be("Existing Event in Cal1");
                reader.GetString(1).Should().Be("""["RRULE:FREQ=DAILY"]""");
                reader.GetString(2).Should().Be("[10]");
            }

            // Now, inserting an event with the same ID into cal-2 MUST succeed (FR-SYNC-1)
            using (var insertDupCmd = conn.CreateCommand())
            {
                insertDupCmd.CommandText = """
                    INSERT INTO Event (id, calendar_id, title, start_utc, end_utc)
                    VALUES ('evt-shared', 'cal-2', 'Same ID Different Cal', '2026-11-03T10:00:00Z', '2026-11-03T11:00:00Z');
                    """;
                await insertDupCmd.ExecuteNonQueryAsync(ct);
            }

            // But inserting an event with the same ID into the same calendar cal-1 MUST fail with primary key violation
            using (var insertConflictCmd = conn.CreateCommand())
            {
                insertConflictCmd.CommandText = """
                    INSERT INTO Event (id, calendar_id, title, start_utc, end_utc)
                    VALUES ('evt-shared', 'cal-1', 'Duplicate in Same Cal', '2026-11-04T10:00:00Z', '2026-11-04T11:00:00Z');
                    """;
                var act = async () => await insertConflictCmd.ExecuteNonQueryAsync(ct);
                await act.Should().ThrowAsync<SqliteException>();
            }

            // Confirm both events exist
            using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM Event WHERE id = 'evt-shared';";
                Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture).Should().Be(2);
            }
        }
    }
}
