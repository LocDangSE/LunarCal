# ADR-0002: Core targets `net10.0`; all other projects target `net10.0-windows10.0.19041.0`

- **Status:** Accepted (2026-10-31)
- **Context:** Spec §2 sets the TFM `net10.0-windows10.0.19041.0`, and spec §3 requires Core to depend on the BCL only.
- **Decision:** `LunarCal.Core` and `LunarCal.Core.Tests` use `net10.0`, the plain BCL with no Windows projections. Infrastructure, App, Tools and the other test projects use the Windows TFM. Both TFMs are defined once in `Directory.Build.props` (`LunarCalCoreTfm`, `LunarCalWindowsTfm`).
- **Consequences:** The compiler blocks Windows APIs in Core, and Core tests can run on any OS. Windows-only projects can still reference Core.
