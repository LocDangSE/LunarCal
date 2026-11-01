# Changelog

## [Unreleased]

### Added
- Phase 0 scaffold: solution, shared build props, Central Package Management, analyzers, CI, docs skeleton.
- Phase 1 lunar engine (`LunarCal.Core.Lunar`): solar↔lunar conversion with Hồ Ngọc Đức's algorithm (UTC+7, 1900–2100), leap months, month lengths, Can Chi of day/month/year, 24 solar terms, per-year cache (FR-LUNAR-1, 2).
- Lunar reference tests against the Hong Kong Observatory table 1901–2100 (2,474 month starts, 4,800 solar terms), the spec's Tết fixtures and Wikipedia; every difference is checked against the UTC+7/UTC+8 offset (FR-LUNAR-3, ADR-0009).
- CI coverage gate `eng/Test-Coverage.ps1`: Lunar ≥ 95%, Core ≥ 85% line coverage.
- ADR-0006..0009.
- Agent handoff docs: `AGENTS.md` (shared by all agents; `CLAUDE.md` imports it), `.agents/rules`, `.agents/workflows` + `.agents/skills` wrappers, `docs/STATUS.md`, `docs/TASKS.md`, `docs/GLOSSARY.md`; ADRs split into `docs/adr/` (ADR-0010..0021).
- Git repository on `main` with remote `https://github.com/LocDangSE/LunarCal.git`; `.gitattributes` line-ending rules, `CODEOWNERS`, PR template (ADR-0023).
- `Dockerfile` (digest-pinned .NET 10 SDK, Linux) that builds and tests `LunarCal.Core`; CI job `core-linux-container`; Dependabot for Docker (ADR-0023).

### Changed
- `.editorconfig`: constants are PascalCase (the `_camelCase` rule applies to other private fields).
