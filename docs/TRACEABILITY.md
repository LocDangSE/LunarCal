# Traceability

Maps requirement IDs from the spec to the tests that cover them. Fill in as each phase lands.

| Requirement | Test(s) | Status / note |
| --- | --- | --- |
| §3 dependency rule (Core = BCL only) | `LunarCal.Core.Tests.ArchitectureTests.Core_references_only_the_base_class_library` | Covered |
| §3 dependency rule (Infrastructure ↛ App/WPF) | `LunarCal.Infrastructure.Tests.ArchitectureTests.Infrastructure_does_not_reference_the_app_or_wpf` | Covered |
| FR-LUNAR-1 conversion API (ToLunar, ToSolar, leap month, month length) | `LunarCal.Core.Tests.Lunar.LunarCalendarTests.FR_LUNAR_1_*` (round-trip and continuity for every day 1900–2100, 12/13-month years, leap 11 of 2033, non-existent dates) | Covered |
| FR-LUNAR-1 range 1900–2100, clear error outside | `LunarCal.Core.Tests.Lunar.LunarCalendarTests.FR_LUNAR_1_Range_is_1900_01_01_to_2100_12_31`, `..._outside_the_range_throw_a_clear_error`, `..._Lunar_dates_whose_solar_date_is_outside_the_range_throw` | Covered (ADR-0006) |
| FR-LUNAR-1 Can Chi of day / month / year | `LunarCal.Core.Tests.Lunar.CanChiTests.*` | Covered (ADR-0006, ADR-0008) |
| FR-LUNAR-1 solar terms (`GetTietKhi`) | `LunarCal.Core.Tests.Lunar.TietKhiTests.*`, `LunarCal.Core.Tests.Lunar.LunarReferenceTests.FR_LUNAR_3_Every_HKO_solar_term_matches_or_differs_only_by_the_time_zone` | Covered (ADR-0007) |
| FR-LUNAR-2 a month in < 5 ms, per-year cache | `LunarCal.Core.Tests.Lunar.LunarPerformanceTests.*` | Covered |
| FR-LUNAR-3 Tết 2023–2026, leap months 2023/2025 | `LunarCal.Core.Tests.Lunar.LunarReferenceTests.FR_LUNAR_3_Tet_falls_on_the_spec_date`, `..._Leap_month_matches_the_spec`, `..._Leap_month_2_of_2023_is_placed_after_month_2` | Covered |
| FR-LUNAR-3 ≥ 200 verified reference points, sources recorded | `LunarCal.Core.Tests.Lunar.LunarReferenceTests.FR_LUNAR_3_Every_HKO_month_start_matches_or_differs_only_by_the_time_zone` (2,474), `..._Tet_every_year_matches_HKO_or_is_one_day_earlier` (200), `..._Every_HKO_solar_term_...` (4,800), `..._Matches_documented_Vietnamese_dates`; sources in `tests/LunarCal.Core.Tests/Fixtures/README.md` | Covered (ADR-0009) |
| Phase 1 DoD: Lunar line coverage ≥ 95% | CI step "Coverage gate" (`eng/Test-Coverage.ps1`) | 100% locally (Debug 217/217, Release 160/160 lines) |
| NFR-MAINT Core line coverage ≥ 85% | CI step "Coverage gate" | 100% locally |
| FR-I18N-1 UI supports vi-VN and en-US, default follows OS, runtime switch | `LunarCal.App.Tests.LocalizationTests.FR_I18N_1_Default_language_follows_OS`, `..._falls_back_to_vietnamese_when_os_culture_is_null`, `..._Runtime_language_switch_updates_immediately_without_restart` | Covered (ADR-0024) |
| FR-I18N-3 Every string in resx (vi-VN and en-US), non-empty, pseudo-localization | `LunarCal.App.Tests.LocalizationTests.FR_I18N_3_Every_key_has_non_empty_vi_and_en`, `..._Pseudo_locale_transforms_every_key`, `..._Pseudo_locale_preserves_format_placeholders_and_formats_cleanly`, `..._Pseudo_locale_can_be_enabled_and_disabled_via_flag_and_culture` | Covered (ADR-0024, P2-02) |
| FR-I18N-7 Mandatory strings in spec §4.11 and toast notifications | `LunarCal.App.Tests.LocalizationTests.FR_I18N_7_Required_spec_table_strings_match_exact_values`, `..._Toast_strings_match_exact_values_and_support_formatting` | Covered (ADR-0024) |
| SEC-KEY-5 Secret scanning (gitleaks on CI and pre-commit hook) | CI job `gitleaks`, `.gitleaks.toml`, `.pre-commit-config.yaml`, `.githooks/pre-commit` | Covered |
| SEC-SC-3 CI static analysis and scanning | CI job `gitleaks` (P0-02), CodeQL workflow `.github/workflows/codeql.yml` (P0-03) | Covered |
| SEC-SC-4 SourceLink for reproducible builds | `Directory.Build.props`, `Directory.Packages.props` (`Microsoft.SourceLink.GitHub`), custom debug info in PDB verified (P0-04) | Covered |
