# ADR-0015: Data lake: outbox cục bộ và ingestion trên cloud

- **Status:** Accepted về kiến trúc (spec §2, FR-DATA, FR-CLOUD). Nhà cung cấp và vùng cloud còn Proposed (spec §14).
- **Bối cảnh:** Dữ liệu lệnh AI (chỉ khi người dùng opt-in) được ghi vào outbox JSONL mã hoá ở máy, rồi upload lên backend dùng chung.
- **Quyết định:** Outbox nằm ở `%LOCALAPPDATA%/LunarCal/datalake/raw/chat_turns/dt=YYYY-MM-DD/part-*.jsonl` (FR-DATA-2), mã hoá AES-256-GCM, khoá bọc DPAPI (FR-DATA-4). Cloud gồm Ingestion API viết bằng ASP.NET Core, PostgreSQL (`contributions`) và object storage (`curated/`, `quarantine/`, Parquet theo ngày). Hạ tầng khai báo bằng IaC trong `infra/` (FR-CLOUD-1).
- **Hệ quả:** Nhà cung cấp cloud (Azure, GCP, AWS hoặc Supabase/Neon), vùng, ngân sách và bên kiểm soát dữ liệu phải hỏi người dùng (spec §14) trước Phase 6b.
