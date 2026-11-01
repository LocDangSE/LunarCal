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
- Phase 2 localization infrastructure: `Strings.vi-VN.resx`, `Strings.en-US.resx`, and neutral `Strings.resx`; runtime culture switching with `LocalizationService` (INotifyPropertyChanged, live indexer binding, `LocExtension` in XAML); OS culture auto-detection; test suite `tests/LunarCal.App.Tests/` covering all mandated spec strings (FR-I18N-1, FR-I18N-3, FR-I18N-7, ADR-0024).
- Gitleaks secret scanning in GitHub Actions CI (`.github/workflows/ci.yml`) and pre-commit configuration (`.pre-commit-config.yaml`, `.githooks/pre-commit`, `.gitleaks.toml`) (SEC-KEY-5, SEC-SC-3).
- CodeQL static analysis workflow for C# on Windows (`.github/workflows/codeql.yml`, action pinned by commit SHA, minimal permissions) (SEC-SC-3).
- SourceLink for reproducible builds and source-level debugging (`Microsoft.SourceLink.GitHub` 10.0.403, `PublishRepositoryUrl`, `EmbedUntrackedSources`, `Deterministic`, `ContinuousIntegrationBuild`) (SEC-SC-4).
- Pseudo-localization support for UI layout overflow testing: `PseudoLocalizer` expanding strings by ~40% with diacritic glyphs and `[ ... ]` delimiters while preserving `{0}` formatting tokens; configurable via `appsettings.json` (`EnablePseudoLocale`) and `ILocalizationService` runtime toggle (FR-I18N-3).
- Settings storage abstraction and in-memory implementation: `ISettingsStore`, `SettingKeys`, `AppSettingDefaults`, and `InMemorySettingsStore` with culture-aware defaults (`lunar_enabled` true, `week_start` Monday for vi-VN and Sunday for en-US, `theme` System, `timezone`) (FR-UI-7, FR-LUNAR-6).

### Changed
- `.editorconfig`: constants are PascalCase (the `_camelCase` rule applies to other private fields).
- Upgraded `github/codeql-action` to version 4.39.1 (`9b3254085494b55a0f7c4c2b3d2d52cc712ce370`) via Dependabot PR #1 for `init` and `analyze` steps (SEC-SC-3).
