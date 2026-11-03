using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Storage;

public class InMemorySettingsStoreTests
{
    [Fact]
    public async Task FR_LUNAR_6_and_FR_UI_7_Default_seeded_store_has_expected_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var defaultsVi = AppSettingDefaults.CreateInitialDefaults("vi-VN");
        var store = new InMemorySettingsStore(defaultsVi);

        (await store.GetLunarEnabledAsync(cancellationToken: ct)).Should().BeTrue("lunar calendar must be enabled by default");
        (await store.GetWeekStartAsync(cancellationToken: ct)).Should().Be(DayOfWeek.Monday, "week_start for vi-VN must default to Monday");
        (await store.GetUiLanguageAsync(cancellationToken: ct)).Should().Be("vi-VN");
        (await store.GetThemeAsync(cancellationToken: ct)).Should().Be("System");
        (await store.GetTimezoneAsync(cancellationToken: ct)).Should().Be(TimeZoneInfo.Local.Id);

        var defaultsEn = AppSettingDefaults.CreateInitialDefaults("en-US");
        var storeEn = new InMemorySettingsStore(defaultsEn);
        (await storeEn.GetWeekStartAsync(cancellationToken: ct)).Should().Be(DayOfWeek.Sunday, "week_start for en-US must default to Sunday");
        (await storeEn.GetUiLanguageAsync(cancellationToken: ct)).Should().Be("en-US");
    }

    [Fact]
    public async Task Setting_CRUD_operations_work_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemorySettingsStore(new Dictionary<string, string>());

        // Get non-existent
        (await store.GetStringAsync("custom_key", ct)).Should().BeNull();
        (await store.GetBoolAsync("custom_flag", defaultValue: true, ct)).Should().BeTrue();
        (await store.GetBoolAsync("custom_flag", defaultValue: false, ct)).Should().BeFalse();

        // Set string
        await store.SetStringAsync("custom_key", "custom_val", ct);
        (await store.GetStringAsync("custom_key", ct)).Should().Be("custom_val");

        // Set bool
        await store.SetBoolAsync("custom_flag", true, ct);
        (await store.GetBoolAsync("custom_flag", cancellationToken: ct)).Should().BeTrue();

        // Overwrite
        await store.SetBoolAsync("custom_flag", false, ct);
        (await store.GetBoolAsync("custom_flag", cancellationToken: ct)).Should().BeFalse();

        // GetAll
        var all = await store.GetAllAsync(ct);
        all.Should().ContainKey("custom_key").WhoseValue.Should().Be("custom_val");
        all.Should().ContainKey("custom_flag").WhoseValue.Should().Be("false");

        // Remove via SetString(null)
        await store.SetStringAsync("custom_key", null, ct);
        (await store.GetStringAsync("custom_key", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Strongly_typed_extensions_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemorySettingsStore();

        // WeekStart
        await store.SetWeekStartAsync(DayOfWeek.Wednesday, ct);
        (await store.GetWeekStartAsync(cancellationToken: ct)).Should().Be(DayOfWeek.Wednesday);

        // LunarEnabled
        await store.SetLunarEnabledAsync(false, ct);
        (await store.GetLunarEnabledAsync(cancellationToken: ct)).Should().BeFalse();

        // Theme
        await store.SetThemeAsync("Dark", ct);
        (await store.GetThemeAsync(cancellationToken: ct)).Should().Be("Dark");

        // Timezone
        await store.SetTimezoneAsync("UTC", ct);
        (await store.GetTimezoneAsync(cancellationToken: ct)).Should().Be("UTC");

        // UiLanguage
        await store.SetUiLanguageAsync("en-US", ct);
        (await store.GetUiLanguageAsync(cancellationToken: ct)).Should().Be("en-US");
    }

    [Fact]
    public void Dependency_injection_registers_ISettingsStore()
    {
        var services = new ServiceCollection();
        var configuration = Substitute.For<IConfiguration>();

        services.AddLunarCalInfrastructure(configuration);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingsStore));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<SqliteSettingsStore>();
    }
}
