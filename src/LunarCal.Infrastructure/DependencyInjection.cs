using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Google.OAuth;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

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

        // Phase 3: Google OAuth desktop client (FR-AUTH-1, FR-AUTH-2, FR-AUTH-6, SEC-OAUTH-1, SEC-OAUTH-2, ADR-0017)
        services.AddSingleton<ISystemBrowser, SystemBrowser>();
        services.AddSingleton<ILoopbackHttpListenerFactory, LoopbackHttpListenerFactory>();
        services.AddSingleton(sp =>
        {
            var options = new GoogleOAuthOptions();

            // Client ID fallback: LunarCal:Google:OAuthClientId -> Google:ClientId -> LunarCal:Google:ClientId
            var clientId = configuration["LunarCal:Google:OAuthClientId"]
                ?? configuration["Google:ClientId"]
                ?? configuration["LunarCal:Google:ClientId"];

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                options.ClientId = clientId;
            }

            // Client Secret fallback: LunarCal:Google:OAuthClientSecret -> Google:ClientSecret -> LunarCal:Google:ClientSecret
            var clientSecret = configuration["LunarCal:Google:OAuthClientSecret"]
                ?? configuration["Google:ClientSecret"]
                ?? configuration["LunarCal:Google:ClientSecret"];

            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                options.ClientSecret = clientSecret;
            }

            if (int.TryParse(configuration["LunarCal:Google:LoopbackTimeoutSeconds"], out var timeoutSec) && timeoutSec > 0)
            {
                options.LoopbackTimeoutSeconds = timeoutSec;
            }

            return options;
        });

        services.AddSingleton<IGoogleOAuthClient>(sp =>
        {
            var options = sp.GetRequiredService<GoogleOAuthOptions>();
            var secretStore = sp.GetRequiredService<ISecretStore>();
            var listenerFactory = sp.GetRequiredService<ILoopbackHttpListenerFactory>();
            var browser = sp.GetRequiredService<ISystemBrowser>();
            var logger = sp.GetService<ILogger>();

            return new GoogleOAuthClient(
                options,
                secretStore,
                listenerFactory,
                browser,
                httpClient: null,
                logger: logger);
        });

        // Phase 3: Google account sign-out and cache wipe service (FR-AUTH-4)
        services.AddSingleton<IGoogleSignOutService, GoogleSignOutService>();

        return services;
    }
}
