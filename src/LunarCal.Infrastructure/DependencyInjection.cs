using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Storage;
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

        return services;
    }
}
