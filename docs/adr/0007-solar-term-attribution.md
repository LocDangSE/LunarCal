# ADR-0007: Solar-term day attribution and precision

- **Status:** Accepted (2026-10-31)
- **Context:** FR-LUNAR-1 requires `TietKhi GetTietKhi(DateOnly)`. Hồ Ngọc Đức's sun-longitude series has no nutation or aberration terms and is accurate to about 0.01° (about 15 minutes). Against HKO, the largest deviation observed in 1901–2100 is 24 minutes.
- **Decision:** A term belongs to the UTC+7 civil day during which the sun reaches its longitude. It is computed from the longitude at the local midnight that ends the day, as in Hồ Ngọc Đức's calendar. `TietKhi.IsStartDay` marks that day. The same series is used for the displayed terms and for the major terms (trung khí) that decide the leap month. A more precise formula used only for display could show a major term inside a month that is marked as leap.
- **Consequences:** A term whose instant falls within about 25 minutes of local midnight may show on the neighbouring day. This affects at most about 0.7% of term days. The reference test bounds this tolerance.
