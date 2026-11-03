using System.Globalization;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Storage;

public sealed class SqliteSettingsStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _secretsDirectory;
    private readonly string _dbPath;

    public SqliteSettingsStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"LunarCal_SettingsTest_{Guid.NewGuid():N}");
        _secretsDirectory = Path.Combine(_testDirectory, "secrets");
        _dbPath = Path.Combine(_testDirectory, "test_lunarcal_settings.db");

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

    private async Task<SqliteConnectionFactory> CreateFactoryAsync(CancellationToken ct)
    {
        var secretStore = new DpapiSecretStore(_secretsDirectory);
        var options = new SqliteStorageOptions { DatabasePath = _dbPath };
        var runner = new SqliteMigrationRunner();
        var factory = new SqliteConnectionFactory(secretStore, options, runner);
        await factory.EnsureInitializedAsync(ct);
        return factory;
    }

    [Fact]
    public async Task FR_UI_7_and_FR_LUNAR_6_Defaults_are_seeded_into_database_on_first_run()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteSettingsStore(factory);

        // Verify seeded defaults
        (await store.GetLunarEnabledAsync(cancellationToken: ct)).Should().BeTrue("lunar calendar must default to true");
        (await store.GetThemeAsync(cancellationToken: ct)).Should().Be("System");
        (await store.GetTimezoneAsync(cancellationToken: ct)).Should().Be(TimeZoneInfo.Local.Id);

        // Verify rows actually exist in the SQLite AppSetting table
        using var connection = await factory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM AppSetting;";
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        count.Should().BeGreaterThan(0, "AppSetting table must have seeded rows");

        // Verify GetAllAsync returns all seeded keys
        var all = await store.GetAllAsync(ct);
        all.Should().ContainKey(SettingKeys.LunarEnabled);
        all.Should().ContainKey(SettingKeys.Theme);
        all.Should().ContainKey(SettingKeys.Timezone);
        all.Should().ContainKey(SettingKeys.WeekStart);
        all.Should().ContainKey(SettingKeys.UiLanguage);
    }

    [Fact]
    public async Task Setting_CRUD_operations_persist_in_sqlite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);
        var store = new SqliteSettingsStore(factory);

        // Non-existent key returns null
        (await store.GetStringAsync("custom_setting_key", ct)).Should().BeNull();

        // Save string
        await store.SetStringAsync("custom_setting_key", "hello_world", ct);
        (await store.GetStringAsync("custom_setting_key", ct)).Should().Be("hello_world");

        // Overwrite string
        await store.SetStringAsync("custom_setting_key", "updated_value", ct);
        (await store.GetStringAsync("custom_setting_key", ct)).Should().Be("updated_value");

        // Boolean get and set
        await store.SetBoolAsync("my_boolean_flag", true, ct);
        (await store.GetBoolAsync("my_boolean_flag", defaultValue: false, cancellationToken: ct)).Should().BeTrue();

        await store.SetBoolAsync("my_boolean_flag", false, ct);
        (await store.GetBoolAsync("my_boolean_flag", defaultValue: true, cancellationToken: ct)).Should().BeFalse();

        // Delete setting by setting null
        await store.SetStringAsync("custom_setting_key", null, ct);
        (await store.GetStringAsync("custom_setting_key", ct)).Should().BeNull();

        // Verify deletion in direct SQL
        using var connection = await factory.CreateConnectionAsync(ct);
        await connection.OpenAsync(ct);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM AppSetting WHERE key = 'custom_setting_key';";
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Settings_persist_across_different_store_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = await CreateFactoryAsync(ct);

        // Store instance 1: writes values
        var store1 = new SqliteSettingsStore(factory);
        await store1.SetThemeAsync("Dark", ct);
        await store1.SetLunarEnabledAsync(false, ct);
        await store1.SetWeekStartAsync(DayOfWeek.Tuesday, ct);
        await store1.SetStringAsync("persisted_test_key", "persisted_value", ct);

        // Store instance 2: reads values from same database
        var store2 = new SqliteSettingsStore(factory);
        (await store2.GetThemeAsync(cancellationToken: ct)).Should().Be("Dark");
        (await store2.GetLunarEnabledAsync(cancellationToken: ct)).Should().BeFalse();
        (await store2.GetWeekStartAsync(cancellationToken: ct)).Should().Be(DayOfWeek.Tuesday);
        (await store2.GetStringAsync("persisted_test_key", ct)).Should().Be("persisted_value");
    }

    [Fact]
    public async Task Fallback_in_memory_mode_works_when_connection_factory_is_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var fallbackStore = new SqliteSettingsStore(connectionFactory: null);

        // Fallback store is pre-seeded with defaults
        (await fallbackStore.GetLunarEnabledAsync(cancellationToken: ct)).Should().BeTrue();
        (await fallbackStore.GetThemeAsync(cancellationToken: ct)).Should().Be("System");

        // Fallback store supports CRUD
        await fallbackStore.SetStringAsync("fallback_key", "fallback_val", ct);
        (await fallbackStore.GetStringAsync("fallback_key", ct)).Should().Be("fallback_val");

        await fallbackStore.SetBoolAsync("fallback_flag", true, ct);
        (await fallbackStore.GetBoolAsync("fallback_flag", cancellationToken: ct)).Should().BeTrue();

        var all = await fallbackStore.GetAllAsync(ct);
        all.Should().ContainKey("fallback_key");

        await fallbackStore.SetStringAsync("fallback_key", null, ct);
        (await fallbackStore.GetStringAsync("fallback_key", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Argument_validation_throws_for_invalid_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new SqliteSettingsStore(connectionFactory: null);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.GetStringAsync("", ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.GetStringAsync("   ", ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SetStringAsync("", "val", ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.GetBoolAsync("", cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SetBoolAsync("", true, ct));
    }
}
