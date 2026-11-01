# ADR-0003: AwesomeAssertions instead of FluentAssertions

- **Status:** Accepted (2026-10-31)
- **Context:** Spec §2 lists FluentAssertions. Since v8, FluentAssertions ships under a commercial licence (Xceed) that is not free for commercial use. v7 is still Apache-2.0 but only receives critical fixes.
- **Decision:** Use AwesomeAssertions, the Apache-2.0 community fork with the same API (namespace `AwesomeAssertions`).
- **Consequences:** No licence risk. Switching back is mostly a namespace change if the owner prefers FluentAssertions.
