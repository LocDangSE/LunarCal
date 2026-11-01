# ADR-0008: Can Chi and solar-term names live in Core in Vietnamese only

- **Status:** Accepted (2026-10-31)
- **Context:** CLAUDE.md requires UI strings to live in resx. FR-I18N-5 says Can Chi names stay Vietnamese in both UI languages, with an English gloss, and that solar terms show both their Vietnamese and English names.
- **Decision:** Core exposes enums (`ThienCan`, `DiaChi`, `SolarTerm`) and the Vietnamese proper names (`CanChi.ToString()`, `TietKhi.VietnameseName`). These are domain names, the same in both UI languages. English glosses and labels (for example "Year of the Fire Horse" or "Start of Winter") are UI strings. They go in `Strings.en-US.resx` / `Strings.vi-VN.resx` in Phase 2, keyed by enum value.
- **Consequences:** Core stays BCL-only and free of localization. Phase 2 must add the resx keys for the 10 stems, 12 branches (animal glosses) and 24 terms.
