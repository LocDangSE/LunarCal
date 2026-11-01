# Data lake

> Skeleton. Filled in Phase 6 (FR-DATA, FR-CLOUD).

- Local outbox: `%LOCALAPPDATA%/LunarCal/datalake/raw/chat_turns/dt=YYYY-MM-DD/part-*.jsonl` (AES-256-GCM, key wrapped with DPAPI).
- Cloud: Ingestion API → PostgreSQL (`contributions`) + object storage (`curated/`, `quarantine/` Parquet by day).
- Schema: spec FR-DATA-3 plus the additional fields in §4.10.
- Example DuckDB queries: TBD.
