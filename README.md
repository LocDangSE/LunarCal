# LunarCal

[![CI](https://github.com/LocDangSE/LunarCal/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/LocDangSE/LunarCal/actions/workflows/ci.yml)

A Windows desktop client for Google Calendar with a Vietnamese lunar calendar and an AI command bar powered by Gemini (bring your own key).

> Status: **Phase 1 (lunar engine) done**. Not yet usable. Current state: `docs/STATUS.md`; agent instructions: `AGENTS.md`.

## Build

Requires the .NET 10 SDK (version pinned in `global.json`):

```powershell
dotnetup install 10
dotnet build LunarCal.slnx
dotnet test --solution LunarCal.slnx
dotnet run --project src/LunarCal.App
```

The domain layer (`LunarCal.Core`) is OS-agnostic and can also be built and tested in a Linux container (the WPF app needs Windows):

```powershell
docker build --target test -t lunarcal-core-test .
```

The spec is in `docs/`; architecture decisions are in `docs/adr/` (index: `docs/DECISIONS.md`).
