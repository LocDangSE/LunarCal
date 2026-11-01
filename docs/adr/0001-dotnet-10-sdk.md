# ADR-0001: .NET 10 SDK pinned via global.json, installed with dotnetup

- **Status:** Accepted (2026-10-31)
- **Context:** Spec §2 requires .NET 10 LTS. The machine has SDK 10.0.403, installed with `dotnetup install 10` (the latest build on the 10 channel at that date).
- **Decision:** `global.json` pins `10.0.403` with `rollForward: latestFeature`, so newer 10.0.x feature bands are accepted.
- **Consequences:** No .NET 8 fallback is needed. CI installs the SDK from `global.json`.
