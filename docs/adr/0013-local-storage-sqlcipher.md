# ADR-0013: Lưu trữ cục bộ bằng SQLite mã hoá SQLCipher

- **Status:** Accepted (2026-11-02, cập nhật chi tiết migration ở task P3-03).
- **Bối cảnh:** Cache lịch, hàng chờ AI, `ReminderState` và setting đều cần lưu bền, mã hoá và có migration (spec §6, SEC-DATA-1, NFR-REL).
- **Quyết định:**
  1. **Thư viện:** Dùng `Microsoft.Data.Sqlite.Core` (10.0.14) kết hợp với `SQLitePCLRaw.bundle_e_sqlcipher` (2.1.11). Khởi tạo `SQLitePCL.Batteries_V2.Init()` ở `SqliteConnectionFactory`.
  2. **Quản lý khoá mã hoá (SEC-DATA-1):** Khoá ngẫu nhiên 256-bit (32 bytes) sinh ở lần chạy đầu bằng `RandomNumberGenerator.GetBytes(32)`, lưu dạng hex và bọc bằng DPAPI thông qua `ISecretStore` (`KnownSecretKeys.DatabaseKey`). Khi DB đã tồn tại mà thiếu khoá trong secret store thì từ chối mở (ném `InvalidOperationException`) để bảo vệ dữ liệu.
  3. **Vị trí lưu:** DB mặc định đặt tại `%LOCALAPPDATA%/LunarCal/lunarcal.db`, thư mục có ACL chỉ cho user hiện tại (SEC-DATA-2, P3-04).
  4. **Cơ chế Migration:**
     - Các migration kế thừa interface `ISqliteMigration` với `Version` số nguyên tuần tự, `Description` và phương thức `UpAsync(connection, transaction, cancellationToken)`.
     - Bảng `__schema_version (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_at TEXT NOT NULL)` theo dõi các version đã áp dụng.
     - `SqliteMigrationRunner` chạy các migration chưa áp dụng theo thứ tự tăng dần, mỗi migration thực thi trong một transaction riêng biệt.
     - `Migration001_InitialSchema` khởi tạo đầy đủ 9 bảng theo spec §6 (`Account`, `CalendarInfo`, `Event`, `LunarRule`, `PendingMutation`, `AppSetting`, `AiCommand`, `ContributionOutbox`, `ReminderState`) và index `idx_event_calendar_start` trên `(calendar_id, start_utc)`.
- **Hệ quả:** Mọi thay đổi schema trong tương lai bắt buộc phải viết migration mới (V2, V3...), không được sửa migration đã phát hành. Kiểm tra bằng unit test nâng cấp schema `Migrations_upgrade_from_empty_to_latest`.
