# Lunar

Vietnamese lunar calendar engine (FR-LUNAR-1..3, Phase 1). Pure C#, offline, thread-safe.

- Algorithm: Hồ Ngọc Đức's, with new moons (sóc) and major solar terms (trung khí), time zone fixed at UTC+7.
- Range: solar dates 1900-01-01..2100-12-31 (lunar years 1899..2100). Outside the range: `ArgumentOutOfRangeException` (ADR-0006).
- Performance: month tables are computed per suì and cached, so a 42-cell month grid takes well under 5 ms (FR-LUNAR-2).

| Type | Role |
| --- | --- |
| `LunarCalendar` | Public API: `ToLunar`, `ToSolar` (returns `null` for dates that do not exist), `GetLeapMonth`, `GetDaysInMonth`, `GetDayCanChi`, `GetMonthCanChi`, `GetYearCanChi`, `GetTietKhi` |
| `LunarDate` | Year, month, day, leap flag |
| `CanChi`, `ThienCan`, `DiaChi` | Sexagenary pair; `ToString()` returns the Vietnamese name (ADR-0008) |
| `TietKhi`, `SolarTerm` | Solar term in effect on a day, plus a start-day flag (ADR-0007) |
| `Astronomy` (internal) | New moon and sun longitude series, month 11 and leap month search |
| `LunarYearTable` (internal) | Cached months of one suì |

The rules for lunar recurrence (`LunarRule`, FR-LUNAR-4, 5) come in Phase 5. Reference data and the reasons it can
differ from HKO are in `tests/LunarCal.Core.Tests/Fixtures/README.md`.
