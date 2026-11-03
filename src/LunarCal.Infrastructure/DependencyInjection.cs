using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;
using LunarCal.Infrastructure.Google.OAuth;
using LunarCal.Infrastructure.Network;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using LunarCal.Infrastructure.Storage.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

        // Settings and the calendar cache both live in the SQLCipher database (spec §6, P3-16, P3-R1).
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<IEventStore, SqliteEventStore>();

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

        // Phase 3: Network security and HTTP resilience pipeline (SEC-NET-1, SEC-NET-2)
        services.AddSingleton(sp =>
        {
            var networkOptions = new NetworkOptions();
            configuration.GetSection(NetworkOptions.SectionName).Bind(networkOptions);

            // Dynamically add data lake ingestion host if configured (SEC-NET-1)
            var ingestionUrl = configuration["LunarCal:DataLake:IngestionBaseUrl"];
            if (!string.IsNullOrWhiteSpace(ingestionUrl) && Uri.TryCreate(ingestionUrl, UriKind.Absolute, out var ingestionUri))
            {
                if (!NetworkSecurityValidator.IsHostAllowed(ingestionUri.Host, networkOptions.AllowedHosts))
                {
                    networkOptions.AllowedHosts.Add(ingestionUri.Host);
                }
            }

            return networkOptions;
        });

        services.AddTransient<NetworkSecurityDelegatingHandler>(sp =>
        {
            var networkOptions = sp.GetRequiredService<NetworkOptions>();
            var logger = sp.GetService<ILogger>();
            return new NetworkSecurityDelegatingHandler(networkOptions, logger);
        });

        // Phase 3: Sync options and clock abstraction for throttling and polling (FR-SYNC-3, FR-SYNC-5)
        services.AddSingleton(sp =>
        {
            var syncOptions = new SyncOptions();
            configuration.GetSection(SyncOptions.SectionName).Bind(syncOptions);
            return syncOptions;
        });

        services.AddSingleton<IClock, SystemClock>();

        services.AddTransient<ThrottlingDelegatingHandler>(sp =>
        {
            var syncOptions = sp.GetRequiredService<SyncOptions>();
            var clock = sp.GetRequiredService<IClock>();
            var logger = sp.GetService<ILogger>();
            return new ThrottlingDelegatingHandler(syncOptions.MaxRequestsPerSecond, clock, logger);
        });

        services.AddHttpClient(ResiliencePipelineExtensions.DefaultClientName)
            .ConfigurePrimaryHttpMessageHandler(() => ResiliencePipelineExtensions.CreateSecureSocketsHttpHandler())
            .AddHttpMessageHandler<NetworkSecurityDelegatingHandler>()
            .AddHttpMessageHandler<ThrottlingDelegatingHandler>()
            .AddLunarCalResilienceHandler();

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
            var httpClientFactory = sp.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory?.CreateClient(ResiliencePipelineExtensions.DefaultClientName);
            var logger = sp.GetService<ILogger>();

            return new GoogleOAuthClient(
                options,
                secretStore,
                listenerFactory,
                browser,
                httpClient,
                logger);
        });

        // Phase 3: Google account sign-out and cache wipe service (FR-AUTH-4)
        services.AddSingleton<IGoogleSignOutService, GoogleSignOutService>();

        // Phase 3: Google Calendar API v3 Gateway and Sync Engine (FR-SYNC-1, FR-SYNC-6, ADR-0028)
        services.AddSingleton<ICalendarGateway>(sp =>
        {
            var httpClientFactory = sp.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory?.CreateClient(ResiliencePipelineExtensions.DefaultClientName)
                ?? new HttpClient();
            var logger = sp.GetService<ILogger>();
            return new CalendarGateway(httpClient, logger);
        });

        services.AddSingleton<ISyncEngine, SyncEngine>();

        // Phase 3: Offline pending mutation store and conflict notification (FR-SYNC-4, NFR-REL)
        services.AddSingleton<IPendingMutationStore, SqlitePendingMutationStore>();
        services.AddSingleton<IConflictNotifier, ConflictNotifier>();
        services.AddSingleton<IMutationService>(sp =>
        {
            var connectionFactory = sp.GetRequiredService<ISqliteConnectionFactory>();
            var mutationStore = sp.GetRequiredService<IPendingMutationStore>();
            var gateway = sp.GetRequiredService<ICalendarGateway>();
            var oauthClient = sp.GetRequiredService<IGoogleOAuthClient>();
            var conflictNotifier = sp.GetRequiredService<IConflictNotifier>();
            var logger = sp.GetService<ILogger>();
            // The optimistic write already lands in the same SQLite database the UI reads (SqliteEventStore),
            // so no second in-memory copy is needed.
            return new MutationService(
                connectionFactory,
                mutationStore,
                gateway,
                oauthClient,
                conflictNotifier,
                syncScheduler: null,
                timeProvider: TimeProvider.System,
                logger: logger,
                eventStore: null);
        });

        // Phase 3: Background sync scheduler (FR-SYNC-3, FR-SYNC-4)
        services.AddSingleton<SyncScheduler>(sp =>
        {
            var syncEngine = sp.GetRequiredService<ISyncEngine>();
            var connectionFactory = sp.GetService<ISqliteConnectionFactory>();
            var syncOptions = sp.GetRequiredService<SyncOptions>();
            var clock = sp.GetRequiredService<IClock>();
            var logger = sp.GetService<ILogger>();
            var scheduler = new SyncScheduler(syncEngine, connectionFactory, syncOptions, clock, logger);
            var mutationService = sp.GetService<IMutationService>();
            if (mutationService is not null)
            {
                scheduler.MutationService = mutationService;
                if (mutationService is MutationService concrete)
                {
                    concrete.SyncScheduler = scheduler;
                }
            }
            return scheduler;
        });
        services.AddSingleton<ISyncScheduler>(sp => sp.GetRequiredService<SyncScheduler>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SyncScheduler>());

        // Phase 3: Network status service (FR-UI-9)
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();

        // Phase 3: Account store (spec §6, FR-AUTH-1, FR-AUTH-4)
        services.AddSingleton<IAccountStore>(sp =>
        {
            var connectionFactory = sp.GetService<ISqliteConnectionFactory>();
            var logger = sp.GetService<ILogger>();
            return new SqliteAccountStore(connectionFactory, logger);
        });

        return services;
    }
}
