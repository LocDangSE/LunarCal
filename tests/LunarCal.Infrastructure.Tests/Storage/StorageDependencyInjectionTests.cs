using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LunarCal.Infrastructure.Tests.Storage;

/// <summary>
/// P3-REVIEW regression: the storage graph built by <see cref="DependencyInjection.AddLunarCalInfrastructure"/> must
/// create the spec §6 schema. DI resolved the migration runner's <c>IEnumerable&lt;ISqliteMigration&gt;</c> parameter
/// as an empty list, so the real app ran zero migrations: no tables, settings and accounts were never saved.
/// </summary>
public sealed class StorageDependencyInjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"LunarCal_DiTest_{Guid.NewGuid():N}");

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

    private ServiceProvider Build() =>
        new ServiceCollection()
            .AddLunarCalInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LunarCal:Storage:DatabasePath"] = Path.Combine(_dir, "app.db"),
                ["LunarCal:Security:SecretStorePath"] = Path.Combine(_dir, "secrets"),
            }).Build())
            .BuildServiceProvider();

    [Fact]
    public async Task SEC_DATA_1_DI_storage_creates_schema_and_settings_survive_a_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);

        using (var first = Build())
        {
            var settings = first.GetRequiredService<ISettingsStore>();
            await settings.SetStringAsync(SettingKeys.FirstRunCompleted, "true", ct);

            await using var connection = await first.GetRequiredService<ISqliteConnectionFactory>().CreateConnectionAsync(ct);
            await connection.OpenAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Account','CalendarInfo','Event','PendingMutation','AppSetting');";
            Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture).Should().Be(5);
        }

        SqliteConnection.ClearAllPools();
        using var second = Build();
        (await second.GetRequiredService<ISettingsStore>().GetStringAsync(SettingKeys.FirstRunCompleted, ct)).Should().Be("true");
    }
}
