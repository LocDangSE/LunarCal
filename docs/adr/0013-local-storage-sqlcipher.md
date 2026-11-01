# ADR-0013: Lưu trữ cục bộ bằng SQLite mã hoá SQLCipher

- **Status:** Accepted (2026-10-31). Spec §2, SEC-DATA-1 đã chốt. Cách làm migration chi tiết còn Proposed (task P3-03).
- **Bối cảnh:** Cache lịch, hàng chờ AI, `ReminderState` và setting đều cần lưu bền, mã hoá và có migration (spec §6).
- **Quyết định:** Dùng `Microsoft.Data.Sqlite` cùng `SQLitePCLRaw.bundle_e_sqlcipher`. Khoá 256-bit ngẫu nhiên sinh ở lần chạy đầu, bọc bằng DPAPI. DB đặt trong `%LOCALAPPDATA%/LunarCal/`, thư mục có ACL chỉ cho user hiện tại (SEC-DATA-2). Migration có version, chạy khi khởi động, có test nâng cấp. Mọi bảng dữ liệu đều có `account_id` hoặc liên kết tới account (FR-AUTH-5).
- **Hệ quả:** Phiên bản package chưa thêm vào `Directory.Packages.props` (CHƯA XÁC MINH phiên bản). Mọi thay đổi schema phải đi kèm migration mới, không sửa migration đã phát hành.
