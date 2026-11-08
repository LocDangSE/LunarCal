using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LunarCal.Infrastructure.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Infrastructure_does_not_reference_the_app_or_wpf()
    {
        var references = typeof(DependencyInjection).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();

        references.Should().NotContain(["LunarCal", "LunarCal.App", "PresentationFramework", "PresentationCore", "WindowsBase"]);
    }

    [Fact]
    public void AddLunarCalInfrastructure_registers_ISystemPromptProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddLunarCalInfrastructure(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISystemPromptProvider));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
