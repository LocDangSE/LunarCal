---
trigger: model_decision
description: Giao diện LunarCal - ánh xạ màn hình Figma sang view trong code, design token, quy ước component WPF/MVVM, theme, accessibility. Đọc khi tạo hoặc sửa View, ViewModel, Control, style, XAML.
---

# Giao diện (UI)

Nguồn: spec §4.3 (FR-UI-1..11), §4.8 (FR-QUEUE), §4.9 (FR-CONFIRM), §4.12 (FR-NOTIFY), §15 (đối chiếu Figma: §15.1 màn hình có sẵn, §15.3 mâu thuẫn và quyết định, §15.4 màn hình cần thiết kế thêm). ADR: `docs/adr/0010`, `0011`.

Figma: file "GCalendar – AI-Powered Calendar (UI v1)", fileKey `4vwWjHGoSu9naj5ou86QYs`, page `Screens` (node `0:1`). Link ở spec §15. Figma v1 là nguồn sự thật cho các màn hình đã vẽ; khi Figma mâu thuẫn với spec thì theo quyết định ở spec §15.3.

## Ánh xạ màn hình → code

Hiện có:
- `src/LunarCal.App/Views/MainWindow.xaml` (Shell layout tích hợp TopBar, Sidebar, Viewport, AI Command Bar) và `ViewModels/MainWindowViewModel.cs`.
- `src/LunarCal.App/Controls/TopBar.xaml`: Brand, nút Hôm nay, mũi tên điều hướng, tiêu đề & phụ đề âm lịch, cụm chuyển view Ngày/Tuần/Tháng/Lịch biểu, nút VI | EN, chip mạng, avatar.
- `src/LunarCal.App/Controls/Sidebar.xaml`: Nút Tạo sự kiện, MiniCalendar, danh sách lịch với checkbox & bullet màu, khối trạng thái đồng bộ ("Đồng bộ ngay", badge lệnh AI chờ).
- `src/LunarCal.App/Controls/MiniCalendar.xaml`: Lưới 6×7 ngày, chuyển tháng, highlight ngày hiện tại & ngày chọn.

Tên view dự kiến ở cột 4 là đề xuất (CHƯA XÁC MINH), chốt khi làm task tương ứng.

| Figma (node) | Nội dung | Yêu cầu | View / ViewModel dự kiến | Task |
| --- | --- | --- | --- | --- |
| 01 · Lịch tháng & trợ lý AI (`1:2`; Sidebar `1:3`, Main `1:196`) | Sidebar, thanh trên cùng, lưới tháng có ngày âm, thanh lệnh AI | FR-UI-1, 2, 5, 9, 10 | `MainWindow` (shell), `Views/MonthView`, `Controls/MiniCalendar`, `Controls/TopBar`, `Controls/Sidebar`, `Controls/AiCommandBar` | P2-06 (shell/controls: xong), P2-07 (lưới tháng), P4-09 (thanh lệnh AI) |
| 02 · AI xác nhận yêu cầu (`2:2`; dialog `2:494`, scrim `2:493`) | Hộp thoại xác nhận một sự kiện | FR-CONFIRM-1..6 | `Views/AiConfirmDialog` + `AiConfirmViewModel` | P4-10 |
| 03 · Offline & hàng chờ AI (`3:2`; drawer `3:493`) | Banner offline, ngăn kéo hàng chờ | FR-QUEUE-2, 3, 6 | `Views/AiQueueDrawer` + `AiQueueViewModel` | P4-09 |
| 04 · Cài đặt & quyền riêng tư (`4:2`; nav `4:3`, content `4:18`) | Chung, Tài khoản Google, AI và dữ liệu huấn luyện | FR-LUNAR-6, FR-I18N-1, FR-BYOK, FR-DATA-1 | `Views/SettingsWindow` có các trang `Settings/General`, `Settings/Account`, `Settings/Ai` | P2-04, P3-15, P4-02, P6a-01 |
| 05 · Nhắc việc Windows (`4:97`; toast `6:270`) | Toast Hoãn và Hoàn thành | FR-NOTIFY-5..8 | Toast XML (không phải view WPF), builder trong App | P7-07, P7-08 |

Màn hình chưa có trong Figma (S06–S20): bảng ở spec §15.4. Phải có thiết kế trước khi làm phase tương ứng (spec §13); nếu chưa có thì ghi vào `docs/STATUS.md` mục "Câu hỏi mở" và dừng task UI đó.

Lưu ý từ spec §15.3: thanh lệnh AI thay cho chat panel; chuỗi `settings.byok.desc` và chuỗi "nhận thay đổi gần như tức thì" đã được sửa theo spec, không theo Figma.

## Design token

- Thư viện style: **ADR-0011 đã chốt** chọn tự định nghĩa hệ thống token bằng ResourceDictionary thuần WPF (`src/LunarCal.App/Resources/Tokens.xaml`), cùng 3 theme (`Themes/Light.xaml`, `Dark.xaml`, `HighContrast.xaml`), quản lý qua `IThemeService`.
- XAML chỉ được tham chiếu token qua `{DynamicResource ...}` (để đổi theme khi đang chạy), không đặt màu hay cỡ chữ hard-code trong view.

### Bảng Token Design System (`Tokens.xaml` & Themes)

| Loại token | Key DynamicResource | Giá trị / Mô tả |
| --- | --- | --- |
| **Typography** | `FontFamilyBase` | Segoe UI, sans-serif |
| | `FontSizeCaption` | 11 pt |
| | `FontSizeBody` | 13 pt |
| | `FontSizeSubtitle` | 14 pt |
| | `FontSizeTitle` | 18 pt |
| | `FontSizeDisplay` | 24 pt |
| | `FontWeightRegular` / `Medium` / `SemiBold` / `Bold` | 400, 500, 600, 700 |
| **Spacing** | `SpacingXSmall` (4), `SpacingSmall` (8), `SpacingMedium` (16), `SpacingLarge` (24), `SpacingXLarge` (32) | Khoảng cách margin/padding chuẩn |
| **Radius** | `CornerRadiusSmall` (4), `CornerRadiusMedium` (8), `CornerRadiusLarge` (12), `CornerRadiusFull` (999) | Bo góc border / button / chip |
| **Metrics** | `TopBarHeight` (56), `SidebarWidth` (240), `AiCommandBarHeight` (48) | Chiều cao / rộng cố định |
| **Brushes (Theme)** | `WindowBackgroundBrush` | Nền chính của cửa sổ |
| | `SurfaceBackgroundBrush` | Nền bề mặt (TopBar, Sidebar) |
| | `SurfaceElevatedBackgroundBrush` | Nền card/dialog nổi bật |
| | `BorderBrush` | Đường viền phân cách |
| | `TextPrimaryBrush`, `TextSecondaryBrush`, `TextTertiaryBrush` | Màu chữ theo cấp độ phân cấp |
| | `AccentPrimaryBrush`, `AccentHoverBrush`, `AccentTextBrush` | Màu nhấn chủ đạo (Google Blue / Tương phản) |
| | `LunarTextPrimaryBrush`, `LunarTextSecondaryBrush` | Màu chữ âm lịch (đỏ gạch / cam đất) |
| | `HoverBackgroundBrush`, `SelectedBackgroundBrush` | Trạng thái hover và active |
| | `TodayHighlightBrush` | Highlight ngày hôm nay |
| | `SuccessBrush`, `WarningBrush`, `ErrorBrush` | Màu trạng thái trực tuyến / cảnh báo / lỗi |

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
