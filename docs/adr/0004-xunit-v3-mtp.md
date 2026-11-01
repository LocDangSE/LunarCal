# ADR-0004: xUnit v3 on Microsoft.Testing.Platform

- **Status:** Accepted (2026-10-31)
- **Context:** xunit.v3 4.x no longer supports VSTest mode under `dotnet test` on .NET 10.
- **Decision:** Opt into Microsoft.Testing.Platform through `global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`). Coverage uses `Microsoft.Testing.Extensions.CodeCoverage`. Test projects are executables (`OutputType=Exe`).
- **Consequences:** The command is `dotnet test --solution LunarCal.slnx`. `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and coverlet are not used.
