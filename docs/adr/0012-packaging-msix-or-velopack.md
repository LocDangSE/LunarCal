# ADR-0012: Đóng gói MSIX hay Velopack

- **Status:** Proposed. Chốt ở Phase 8 (task P8-01).
- **Bối cảnh:** Spec §2 yêu cầu chọn một trong hai. Spec §4.12 nêu ràng buộc toast: AUMID cố định (FR-NOTIFY-10), COM activator, `ToastNotificationManagerCompat.Uninstall()` khi gỡ cài đặt (Velopack), và khởi động nền chỉ để xử lý nút bấm toast. Còn phụ thuộc spec §14 câu 1 (tên app, nhà phát hành) và câu 3 (chứng chỉ ký, kênh cập nhật).
- **Hệ quả:** Lựa chọn này quyết định cách đăng ký toast, auto-update (SEC-REL-2) và cách ký (SEC-REL-1). Đừng đổi AUMID sau khi đã phát hành.
