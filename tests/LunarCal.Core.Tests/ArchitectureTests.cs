using LunarCal.Core;

namespace LunarCal.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Core_references_only_the_base_class_library()
    {
        var references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

        references
            .Select(r => r.Name)
            .Should()
            .OnlyContain(name => name == "netstandard" || name == "mscorlib" || name!.StartsWith("System", StringComparison.Ordinal),
                "Core must stay free of UI/IO dependencies (spec §3)");
    }
}
