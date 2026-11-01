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
- Calendar domain models and event store port: `CalendarInfo` (with `AccountId` for multi-account ready support), `CalendarEvent` (with all spec §6 and FR-SYNC-6 fields: `RecurringEventId`, `Etag`, `Updated`, `ExtendedProperties`), port `IEventStore`, and thread-safe `InMemoryEventStore` seeded with realistic sample data for UI shell (FR-UI-1, FR-AUTH-5, FR-SYNC-6).
- Theme styling system and design tokens (ADR-0011): `Tokens.xaml` for typography, spacing, radius, and layout metrics; `Themes/Light.xaml`, `Dark.xaml`, and `HighContrast.xaml` palettes; `IThemeService` and `ThemeService` for runtime theme switching, Windows system theme tracking, and high contrast accessibility support (FR-UI-7, FR-UI-8).
- MainWindow UI shell and controls based on Figma 01: `MainWindow.xaml` shell layout, `TopBar.xaml` (brand, GoToToday button, period navigation, header title with lunar subtitle, view mode switcher Day/Week/Month/Agenda, runtime language switcher VI | EN, network status indicator, and user avatar), `Sidebar.xaml` (Create Event button, MiniCalendar with 6×7 day grid and month navigation, My Calendars list with color bullets and checkboxes, and sync status block with manual sync trigger and pending AI command badge), and comprehensive view model test suite (FR-UI-1, FR-UI-9, FR-UI-10).
- Phase 2 screen designs in Figma (S06 Week, S07 Day, S08 Agenda, S09 Day details, S13 Appearance Settings, Day detail panel and Day cell components) and design token system documentation in ui.md measured from Figma 01–05 (FR-UI-11, P2-00).

### Changed
- `.editorconfig`: constants are PascalCase (the `_camelCase` rule applies to other private fields).
- Upgraded `github/codeql-action` to version 4.39.1 (`9b3254085494b55a0f7c4c2b3d2d52cc712ce370`) via Dependabot PR #1 for `init` and `analyze` steps (SEC-SC-3).
