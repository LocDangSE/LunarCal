---
trigger: model_decision
description: Giao diện LunarCal - ánh xạ màn hình Figma sang view trong code, design token, quy ước component WPF/MVVM, theme, accessibility. Đọc khi tạo hoặc sửa View, ViewModel, Control, style, XAML.
---

# Giao diện (UI)

Nguồn: spec §4.3 (FR-UI-1..11), §4.8 (FR-QUEUE), §4.9 (FR-CONFIRM), §4.12 (FR-NOTIFY), §15 (đối chiếu Figma: §15.1 màn hình có sẵn, §15.3 mâu thuẫn và quyết định, §15.4 màn hình cần thiết kế thêm). ADR: `docs/adr/0010`, `0011`.

Figma: file "GCalendar – AI-Powered Calendar (UI v1)", fileKey `4vwWjHGoSu9naj5ou86QYs`, page `Screens` (node `0:1`). Link ở spec §15. Figma v1 là nguồn sự thật cho các màn hình đã vẽ; khi Figma mâu thuẫn với spec thì theo quyết định ở spec §15.3.

## Ánh xạ màn hình → code

Hiện chỉ có `src/LunarCal.App/Views/MainWindow.xaml` (`Grid` rỗng) và `ViewModels/MainWindowViewModel.cs`. Tên view dự kiến ở cột 4 là đề xuất (CHƯA XÁC MINH), chốt khi làm task tương ứng.

| Figma (node) | Nội dung | Yêu cầu | View / ViewModel dự kiến | Task |
| --- | --- | --- | --- | --- |
| 01 · Lịch tháng & trợ lý AI (`1:2`; Sidebar `1:3`, Main `1:196`) | Sidebar, thanh trên cùng, lưới tháng có ngày âm, thanh lệnh AI | FR-UI-1, 2, 5, 9, 10 | `MainWindow` (shell), `Views/MonthView`, `Controls/MiniCalendar`, `Controls/TopBar`, `Controls/Sidebar`, `Controls/AiCommandBar` | P2-06, P2-07, P4-09 |
| 02 · AI xác nhận yêu cầu (`2:2`; dialog `2:494`, scrim `2:493`) | Hộp thoại xác nhận một sự kiện | FR-CONFIRM-1..6 | `Views/AiConfirmDialog` + `AiConfirmViewModel` | P4-10 |
| 03 · Offline & hàng chờ AI (`3:2`; drawer `3:493`) | Banner offline, ngăn kéo hàng chờ | FR-QUEUE-2, 3, 6 | `Views/AiQueueDrawer` + `AiQueueViewModel` | P4-09 |
| 04 · Cài đặt & quyền riêng tư (`4:2`; nav `4:3`, content `4:18`) | Chung, Tài khoản Google, AI và dữ liệu huấn luyện | FR-LUNAR-6, FR-I18N-1, FR-BYOK, FR-DATA-1 | `Views/SettingsWindow` có các trang `Settings/General`, `Settings/Account`, `Settings/Ai` | P2-04, P3-15, P4-02, P6a-01 |
| 05 · Nhắc việc Windows (`4:97`; toast `6:270`) | Toast Hoãn và Hoàn thành | FR-NOTIFY-5..8 | Toast XML (không phải view WPF), builder trong App | P7-07, P7-08 |

Màn hình chưa có trong Figma (S06–S20): bảng ở spec §15.4. Phải có thiết kế trước khi làm phase tương ứng (spec §13); nếu chưa có thì ghi vào `docs/STATUS.md` mục "Câu hỏi mở" và dừng task UI đó.

Lưu ý từ spec §15.3: thanh lệnh AI thay cho chat panel; chuỗi `settings.byok.desc` và chuỗi "nhận thay đổi gần như tức thì" đã được sửa theo spec, không theo Figma.

## Design token

- File Figma UI v1 **không có variable hay design token nào** (kiểm tra bằng Figma variables cho node `1:2` ngày 2026-10-31: kết quả rỗng). Màu, font, spacing, radius hiện là giá trị đặt thẳng trong layer: CHƯA XÁC MINH.
- Việc cần làm (task P2-00): trích màu, typography, spacing, radius từ các frame 01–05, ghi thành ResourceDictionary `src/LunarCal.App/Resources/Tokens.xaml` (tên đề xuất). Mỗi token có biến thể sáng, tối và High Contrast.
- XAML chỉ được tham chiếu token qua `{DynamicResource ...}` (để đổi theme khi đang chạy), không đặt màu hay cỡ chữ thẳng trong view.
- Thư viện style (WPF UI hay tự style): ADR-0011, chưa chốt.

## Quy ước component (WPF + MVVM)

- MVVM bằng `CommunityToolkit.Mvvm`: `[ObservableProperty]`, `[RelayCommand]`; view model là `partial` và kế thừa `ObservableObject`. Không đặt logic trong code-behind, ngoài `InitializeComponent` và gán `DataContext`.
- View model đăng ký trong DI (`src/LunarCal.App/App.xaml.cs`); view nhận view model qua constructor.
- Command bất đồng bộ dùng `[RelayCommand]` trên method trả `Task`, không dùng `async void` (spec §5.9). Không block UI thread bằng `.Result` hay `.Wait()`; mọi gọi mạng phải async (NFR-PERF).
- Control tái sử dụng đặt ở `src/LunarCal.App/Controls/`; style và theme ở `src/LunarCal.App/Resources/`.
- Chuỗi chỉ lấy từ resx (`.agents/rules/i18n.md`).
- Lịch âm hiển thị qua `LunarCal.Core.Lunar.LunarCalendar`. Ẩn mọi thành phần âm lịch khi công tắc `lunar_enabled` tắt (FR-LUNAR-6). Mùng 1 hiển thị dạng `1/tháng`, có ký hiệu tháng nhuận (FR-UI-2).

## Theme, DPI, accessibility

- Sáng, tối, theo hệ thống; Per-Monitor V2 DPI (đã khai báo `dpiAwareness = PerMonitorV2` trong `src/LunarCal.App/app.manifest`); High Contrast (FR-UI-7, FR-UI-8).
- Điều hướng đầy đủ bằng bàn phím. Mọi control chính có `AutomationProperties.Name` và `AutomationProperties.AutomationId`, để FlaUI test tìm được (cửa sổ chính: `MainWindow`).
- Tương phản ≥ 4.5:1, vùng bấm đủ lớn, có trạng thái focus (spec §15.2, dòng FR-UI-8).

## Hiệu năng

Render view Tháng < 100 ms, khởi động nguội < 2 giây khi có cache (NFR-PERF). Lịch âm của một tháng < 5 ms (FR-LUNAR-2, đã đạt).
