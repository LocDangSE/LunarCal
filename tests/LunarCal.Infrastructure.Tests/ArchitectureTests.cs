using LunarCal.Infrastructure;

namespace LunarCal.Infrastructure.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Infrastructure_does_not_reference_the_app_or_wpf()
    {
        var references = typeof(DependencyInjection).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();

        references.Should().NotContain(["LunarCal", "LunarCal.App", "PresentationFramework", "PresentationCore", "WindowsBase"]);
    }
}
