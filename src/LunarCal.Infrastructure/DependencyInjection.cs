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

        return services;
    }
}
