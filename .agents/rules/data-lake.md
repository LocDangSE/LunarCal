---
trigger: model_decision
description: Data lake LunarCal - dữ liệu nào được thu thập, điều kiện opt-in, schema record, outbox mã hoá, upload lên cloud, xem và xoá. Đọc khi đụng tới DataLake, consent, outbox, uploader, backend ingestion hoặc infra/.
---

# Data lake và đóng góp dữ liệu

Nguồn: spec §4.7 (FR-DATA-1..7), §4.10 (FR-CLOUD-1..10 và bảng trường bổ sung), §5.11 (SEC-CLOUD-1..8), FR-CONFIRM-9, §6 (`ContributionOutbox`, `AppSetting`). ADR: `docs/adr/0015`. Tài liệu chi tiết: `docs/DATA_LAKE.md` (hiện là khung, điền ở Phase 6).

## Trạng thái hiện tại

Chưa có code. `src/LunarCal.Infrastructure/DataLake/` mới có README, `src/LunarCal.Tools/Program.cs` là placeholder. Làm ở Phase 6a/6b/6c (`docs/TASKS.md`).

## Điều kiện thu thập: bắt buộc

- **Chỉ khi người dùng đã opt-in** (setting `training_opt_in`, mặc định tắt). Chưa opt-in thì không ghi outbox, không upload, không tạo `contributor_id` (FR-DATA-1, FR-DATA-2, FR-CLOUD-2).
- Màn hình đồng ý hiện ở lần đầu dùng chat, mặc định không chọn. Chuỗi bắt buộc: `consent.*`, `settings.training.*` (spec §4.11).
- Đồng ý có phiên bản: mỗi record mang `consent_version`, thời điểm đồng ý và `consent_lang`. Nội dung thu thập thay đổi thì phải hỏi đồng ý lại trước khi upload tiếp (FR-CLOUD-5).
- Tắt opt-in: dừng upload ngay, xoá phần outbox chưa gửi, và hỏi người dùng có muốn xoá dữ liệu đã gửi không (FR-CLOUD-2, FR-CLOUD-5).
- Kill switch trong cấu hình từ xa để dừng upload mà không cần phát hành bản mới (SEC-CLOUD-7).

## Thu thập gì

- Một record cho mỗi lệnh AI được xác nhận, sửa, từ chối hoặc huỷ (FR-DATA-2, FR-CONFIRM-9).
- Schema: bảng trường ở FR-DATA-3 cộng bảng "Trường bổ sung" ở §4.10. Có `schema_version` và `prompt_version`.
- Tách trường do người dùng tạo (`user_text`, `user_edits`, `final_events`) khỏi trường do model sinh (`model_output_raw`); gắn `source_per_field` (FR-DATA-6, FR-CLOUD-9).
- Gửi cho Gemini chỉ gồm câu lệnh, tên calendar, locale, mốc thời gian (spec §7). Lake không chứa nội dung event ngoài chat, trừ khi người dùng bật rõ ràng (FR-DATA-7).

## Không bao giờ ghi vào lake

API key, OAuth token, header Authorization, nội dung event ngoài chat (FR-DATA-7, FR-CLOUD-8). Có test quét lake sau một kịch bản đầy đủ (spec §9, Phase 6 DoD).

## Xử lý trên máy (client)

1. Ẩn danh hoá trước khi ghi outbox và trước khi upload: loại tên, địa điểm, email, số điện thoại; ghi `pii_flags` (FR-DATA-4, SEC-CLOUD-4). Bộ test PII ghi trong `docs/PII_TESTS.md` (chưa tạo; ngưỡng đạt do chủ dự án đặt).
2. Outbox: JSONL append-only tại `%LOCALAPPDATA%/LunarCal/datalake/raw/chat_turns/dt=YYYY-MM-DD/part-*.jsonl`, mã hoá AES-256-GCM, khoá bọc DPAPI (FR-DATA-2, FR-DATA-4). `LunarCal:DataLake:RootPath` trong `src/LunarCal.App/appsettings.json`. Bảng theo dõi: `ContributionOutbox` (spec §6).
3. Upload: `IContributionUploader` (Core) gửi theo lô (tối đa 100 record hoặc 1 MB, gzip), idempotent theo `turn_id`, retry có backoff, chỉ chạy khi có mạng và app idle (FR-CLOUD-2). Giới hạn lấy từ `LunarCal:Cloud:*`.
4. Định danh: `contributor_id` là UUID ngẫu nhiên, không liên kết với Google hay email. Đổi lấy token ngắn hạn (≤ 1 giờ) bằng bí mật thiết bị lưu DPAPI, không đặt cùng chỗ với outbox (FR-CLOUD-3, SEC-CLOUD-2). Dữ liệu là đã giả danh hoá, không phải ẩn danh tuyệt đối; chuỗi giao diện phải nói đúng như vậy.

## Cloud (Phase 6b)

- Ingestion API viết bằng ASP.NET Core .NET 10 → PostgreSQL (`contributions`) và object storage (`curated/`, `quarantine/`, Parquet theo ngày). IaC trong `infra/` (FR-CLOUD-1). Nhà cung cấp và vùng: hỏi chủ dự án (spec §14), ghi vào ADR-0015.
- Server không tin client: kiểm tra schema, tối đa 64 KB mỗi record, từ chối trường lạ, rate limit, quét PII lần hai, record còn PII vào `quarantine` (FR-CLOUD-6, SEC-CLOUD-3).
- Compaction: JSONL → Parquet theo ngày, dedupe theo `turn_id`, file lỗi vào `quarantine/` (FR-DATA-5).
- Xem và xoá: API trả record của chính `contributor_id`; xoá trong Postgres và Parquet, ghi tombstone, backup bị xoá theo chu kỳ ≤ 30 ngày; có thao tác thu hồi `contributor_id` (FR-CLOUD-4, SEC-CLOUD-1).
- Dữ liệu thô và curated nằm ở hai vùng quyền khác nhau; RBAC, audit, KMS (SEC-CLOUD-5, FR-CLOUD-8).
- Dataset: chỉ gồm record có consent hợp lệ, đã quét PII, không tombstone; chia train/val/test theo hash `contributor_id`; báo cáo theo `input_lang` (FR-CLOUD-7, Phase 6c).

## Pháp lý

Trước khi phát hành phải có luật sư rà soát (FR-CLOUD-10). Bên kiểm soát dữ liệu, liên hệ và độ tuổi tối thiểu: hỏi chủ dự án (spec §14).
