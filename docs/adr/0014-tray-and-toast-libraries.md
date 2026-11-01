# ADR-0014: Thư viện tray và toast

- **Status:** Accepted về thư viện (spec §2). AUMID cụ thể còn Proposed.
- **Bối cảnh:** Spec §2 chọn `H.NotifyIcon.Wpf` cho tray và `Microsoft.Toolkit.Uwp.Notifications` cho toast. FR-NOTIFY-10 yêu cầu AUMID cố định, không phụ thuộc tên thương mại.
- **Quyết định:** Dùng hai thư viện trên ở Phase 7. AUMID chốt cùng ADR-0012 và trước bản phát hành đầu tiên.
- **Hệ quả:** Phiên bản package CHƯA XÁC MINH. Phải kiểm tra package còn được bảo trì trên .NET 10 trước khi thêm; nếu không còn thì ghi một ADR mới thay thế ADR này.
