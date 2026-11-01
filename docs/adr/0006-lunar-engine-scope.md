# ADR-0006: Lunar engine scope, range and API semantics

- **Status:** Accepted (2026-10-31)
- **Context:** Spec FR-LUNAR-1 requires Hồ Ngọc Đức's algorithm with the time zone fixed at UTC+7, supports 1900–2100, and asks for a clear error outside that range. Some details are open: whether the range is solar or lunar, what to return for lunar dates that do not exist, the Can Chi of a leap month, and how to handle years before 1968, when Vietnamese calendars still used UTC+8.
- **Decision:**
  - The range is defined on solar dates: 1900-01-01..2100-12-31. The lunar-to-solar methods accept lunar years 1899..2100, because 1900-01-01 falls in lunar year 1899. If the resulting solar date is outside the range, they throw.
  - Out-of-range input and malformed arguments (month not 1–12, day not 1–30) throw `ArgumentOutOfRangeException` with the supported range in the message. A well-formed lunar date that does not exist returns `null`: a leap month the year does not have, or day 30 of a 29-day month.
  - UTC+7 is used for the whole range, including years before 1968, as the spec requires. The fixtures README documents the possible differences from calendars printed at the time.
  - A leap month has the same Can Chi as the regular month with that number. Month Can Chi follows the standard formula (month 1 = Dần; the stem follows the year stem, ngũ hổ độn).
  - `LunarCalendar` is a static, thread-safe API. Month tables are computed per suì (winter solstice to winter solstice) and cached in a `ConcurrentDictionary`, which covers FR-LUNAR-2.
- **Consequences:** Callers get a total function over the solar range and can tell "does not exist" (`null`) from "unsupported" (exception). If the App ever needs a DI seam, a thin `ILunarCalendar` wrapper can be added in Core.
