# ADR-0011: Thư viện style WPF (WPF UI/Fluent hay tự style)

- **Status:** Accepted (2026-11-01, task P2-03).
- **Bối cảnh:** Spec §2 cho chọn giữa "WPF UI (Fluent) hoặc tự style". Spec FR-UI-7 và FR-UI-8 yêu cầu theme sáng/tối/theo hệ thống, High Contrast, Per-Monitor V2 DPI. Giao diện LunarCal theo Figma UI v1 có các control chuyên biệt cho lịch: lưới tháng 6×7, ngày âm đỏ/xám, mùng 1 dạng `1/tháng`, sidebar, mini-calendar và thanh lệnh AI.
- **Các phương án xem xét:**
  1. **Thư viện WPF UI (Lepoco.WPFUI / FluentWPF):** Cung cấp sẵn các control kiểu Windows 11 Fluent, nhưng mang theo phụ thuộc bên thứ ba vào solution .NET 10 (`net10.0-windows10.0.19041.0`), có nguy cơ xung đột chu kỳ phát hành với .NET 10 preview/patch, và vẫn không có sẵn các component lịch chuyên biệt của dự án.
  2. **Tự style bằng ResourceDictionary thuần WPF:** Tận dụng hệ thống `ResourceDictionary` của WPF trong `src/LunarCal.App/Resources/Tokens.xaml` và `src/LunarCal.App/Themes/` (`Light.xaml`, `Dark.xaml`, `HighContrast.xaml`).
- **Quyết định:** Chọn **Phương án 2: Tự style bằng ResourceDictionary thuần WPF**.
  - Không thêm dependency bên ngoài, hoàn toàn tự chủ mã nguồn và tương thích tuyệt đối với .NET 10 SDK ghim trong `global.json`.
  - Toàn quyền kiểm soát và tùy biến linh hoạt các component lịch phức tạp (MonthView, WeekView, DayView, MiniCalendar).
  - Tối ưu hiệu năng: ResourceDictionary thuần tải nhanh, đáp ứng tiêu chí NFR-PERF (khởi động nguội < 2s).
  - Hỗ trợ High Contrast trực tiếp thông qua các cọ hệ thống (`SystemColors.*`) theo chuẩn accessibility Windows (FR-UI-8).
  - Hỗ trợ đổi theme thời gian thực (runtime theme switching) qua `IThemeService` bằng cách hoán đổi ResourceDictionary trong `Application.Current.Resources.MergedDictionaries`.
- **Hệ quả:**
  - Token giao diện dùng chung được định nghĩa tại `src/LunarCal.App/Resources/Tokens.xaml`.
  - Ba bảng màu theme đặt tại `src/LunarCal.App/Themes/Light.xaml`, `Dark.xaml`, `HighContrast.xaml`.
  - Mọi view và control chỉ tham chiếu token màu qua `{DynamicResource TokenKey}` để theme đổi tức thì mà không cần khởi động lại app.
