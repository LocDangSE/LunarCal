using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LunarCal.Infrastructure;

/// <summary>Registers infrastructure services (Google, Gemini, storage, security, data lake).</summary>
public static class DependencyInjection
{
    /// <summary>Adds LunarCal infrastructure services. Registrations are added phase by phase (spec §13).</summary>
    public static IServiceCollection AddLunarCalInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Phase 2: In-memory settings and event stores; Phase 3 transitions to SQLCipher SQLite
        services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        // Phase 3: DPAPI secret store with app-specific entropy (SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3, ADR-0018)
        services.AddSingleton<ISecretStore>(sp =>
        {
            string? secretPath = configuration["LunarCal:Security:SecretStorePath"];
            if (!string.IsNullOrWhiteSpace(secretPath))
            {
                secretPath = Environment.ExpandEnvironmentVariables(secretPath);
                return new DpapiSecretStore(secretPath);
            }

            return new DpapiSecretStore();
        });

        // Phase 3: SQLCipher encrypted local database & migration runner (SEC-DATA-1, spec §6, ADR-0013)
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<ISqliteConnectionFactory>(sp =>
        {
            var secretStore = sp.GetRequiredService<ISecretStore>();
            var runner = sp.GetRequiredService<SqliteMigrationRunner>();

            var storageOptions = new SqliteStorageOptions();
            string? configuredDbPath = configuration["LunarCal:Storage:DatabasePath"];
            if (!string.IsNullOrWhiteSpace(configuredDbPath))
            {
                storageOptions.DatabasePath = configuredDbPath;
            }

            return new SqliteConnectionFactory(secretStore, storageOptions, runner);
        });

        // Phase 3: Local data wipe service (SEC-DATA-3)
        services.AddSingleton<ILocalDataWipeService, LocalDataWipeService>();

        return services;
    }
}
