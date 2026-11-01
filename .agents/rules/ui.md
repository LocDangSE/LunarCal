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
| S06 · Tuần (`23:2`; body `24:2`) | Lưới 7 cột theo giờ (48 px/giờ), hàng cả ngày, vạch giờ hiện tại, ngày âm cạnh ngày dương ở tiêu đề cột, sự kiện trùng giờ chia làn | FR-UI-1 | `Views/WeekView` + `WeekViewModel` | P2-08 |
| S07 · Ngày (`23:493`; body `25:48`) | Như S06 một cột, panel chi tiết ngày gắn bên phải | FR-UI-1, FR-UI-3 | `Views/DayView` + `DayViewModel` | P2-08 |
| S08 · Lịch biểu (`23:984`; body `25:145`) | Thanh lọc calendar (chip), danh sách nhóm theo ngày, ngày âm, mùng 1 in đậm | FR-UI-1 | `Views/AgendaView` + `AgendaViewModel` | P2-09 |
| S09 · Chi tiết ngày (`23:1475`; trạng thái `26:127`; component `Day detail panel` `25:2`, `Day cell` `26:126`) | Panel 360 px: khối âm lịch, can chi ngày/tháng/năm, tiết khí, sự kiện, nút thêm sự kiện / luật âm lịch. Ô ngày ở 7 trạng thái, gồm mùng 1 tháng nhuận (`1/6n`) và tắt lịch âm | FR-UI-2, FR-UI-3, FR-LUNAR-6 | `Views/DayDetailPanel` + `DayDetailViewModel`, `Controls/DayCell` | P2-07, P2-10 |
| S13 · Cài đặt > Giao diện (`23:1966`) | Chủ đề (Sáng/Tối/Theo hệ thống), mật độ, ngày đầu tuần, 12h/24h, múi giờ hiển thị, chi tiết âm lịch | FR-UI-7, FR-I18N-4 | `Views/Settings/AppearancePage` | P2-11 |
| S10 · Trình soạn sự kiện (`35:218`; dialog `35:774`) | Hộp thoại 560 px: tiêu đề, ngày giờ kèm ngày âm, cả ngày, múi giờ, lặp (Không lặp/Dương lịch/Âm lịch, khối luật âm lịch), nhắc nhở dạng chip, lịch đích (kèm đồng bộ lên Google cho luật âm lịch), địa điểm, mô tả; footer Xoá / Huỷ / Lưu (Ctrl+S) | FR-UI-4, FR-LUNAR-5 | `Views/EventEditorDialog` + `EventEditorViewModel` | P3-14 |
| S10 · Menu ngữ cảnh (`37:627`; menu sự kiện `37:1003`, menu ô trống `37:1019`) | Chuột phải lên sự kiện (Mở, Sửa, Nhân bản, Chuyển lịch, Đổi thành lặp âm lịch, Xoá) và lên ô trống (Tạo lúc 14:00, Tạo cả ngày, Tạo luật âm lịch, Dán); mở bằng Shift+F10 | FR-UI-4, FR-UI-8 | `ContextMenu` trong `WeekView`/`DayView`/`MonthView` | P3-14 |
| S14 · Lần chạy đầu 1/3, 2/3, 3/3 (`39:280`, `39:336`, `39:395`) | Cửa sổ 960×640 có stepper: (1) ngôn ngữ + Hiển thị Âm lịch, (2) đăng nhập Google, trạng thái chờ trình duyệt 5 phút, cảnh báo "chưa xác minh" và Testing 7 ngày, (3) hướng dẫn lấy Gemini key, nút Kiểm tra key (tuỳ chọn) | FR-AUTH-1, FR-AUTH-6, FR-BYOK-1..3, FR-I18N-1 | `Views/FirstRunWindow` + `FirstRunViewModel` | P3-15 (bước 1–2), P4-03 (bước 3) |
| S14 · Đồng ý thu thập dữ liệu (`40:276`; dialog `40:832`) | Hiện một lần trước lệnh AI đầu tiên (FR-DATA-1), không nằm trong lần chạy đầu. Công tắc mặc định tắt, chuỗi đúng FR-I18N-7, nhãn phiên bản đồng ý | FR-DATA-1, FR-I18N-7, FR-CLOUD-5 | `Views/ConsentDialog` | P6a-01 |
| S14 · Đăng nhập lại (`40:863`; dialog `40:1419`, banner `40:1444`) | Banner cảnh báo + hộp thoại khi `invalid_grant`: giải thích Testing 7 ngày, số thay đổi chờ đồng bộ, Để sau / Đăng nhập lại | FR-AUTH-3, FR-AUTH-6 | `Views/ReauthDialog`, banner trong `MainWindow` | P3-06, P3-15 |
| S18 · Trạng thái lỗi, rỗng, đang tải (`42:292`) | Banner: token hết hạn, xung đột 412, lỗi/giới hạn đồng bộ, đã phục hồi; thanh lệnh AI: key sai (401/403), 429, bị chặn an toàn, chưa có key; hộp thoại chi tiết xung đột (Google thắng); rỗng: không có calendar, không có sự kiện, đang ẩn mọi lịch; skeleton lưới tháng, tiến độ đồng bộ đầu | FR-SYNC-4, FR-SYNC-5, FR-AUTH-3, FR-BYOK, FR-QUEUE | `Controls/StatusBanner`, `Views/SyncConflictDialog`, empty state trong từng view | P3-13, P3-15, P4-09 |
| S20 · Cài đặt > Nâng cao (`34:191`) | Tài khoản Google (nhiều tài khoản, FR-AUTH-5 để phase sau), OAuth client riêng (Client ID, client secret, ghi chú Testing), model Gemini (danh sách lấy từ API, không hard-code ID), vị trí và dung lượng dữ liệu, xoá toàn bộ dữ liệu cục bộ, nhật ký (mức log, thư mục, giữ 14 ngày) | FR-AUTH-5, FR-AUTH-6, SEC-DATA-3, SEC-LOG-1 | `Views/Settings/AdvancedPage` | P3-04, P3-15, P4-02 |
| Component P3 (`33:191`) | Button (Primary/Secondary/Danger/Ghost), Field, Toggle, Checkbox, Radio, Banner (Warning/Danger/Info/Success), Skeleton/Bar, Menu item | | `Resources/Styles` | P3-14, P3-15 |

S06–S09 và S13 là bản nháp Claude Code vẽ ngày 2026-11-01, **chờ chủ dự án duyệt** (P2-00). Dữ liệu mẫu (16/10/2026 = mùng 7 tháng 9 Bính Ngọ, ngày Quý Hợi, tháng Mậu Tuất, tiết Hàn lộ) đã tính tay để khớp với engine. Ký hiệu tháng nhuận `n` (ví dụ `1/6n`) là đề xuất.

S10, S14, S18, S20 và component P3 là bản nháp Claude Code vẽ ngày 2026-11-01 (P3-00b), **chờ chủ dự án duyệt**. Vẽ bằng giá trị trong bảng token bên dưới (file vẫn chưa có Variables). Ngày âm mẫu tính bằng engine: 22/10/2026 = 13/9 Bính Ngọ, các lần sau 12/10/2027, 30/10/2028.

Màn hình chưa có trong Figma (S11, S12, S15–S17, S19): bảng ở spec §15.4. Phải có thiết kế trước khi làm phase tương ứng (spec §13); nếu chưa có thì ghi vào `docs/STATUS.md` mục "Câu hỏi mở" và dừng task UI đó.

Lưu ý từ spec §15.3: thanh lệnh AI thay cho chat panel; chuỗi `settings.byok.desc` và chuỗi "nhận thay đổi gần như tức thì" đã được sửa theo spec, không theo Figma.

## Design token

- Thư viện style: **ADR-0011 đã chốt** chọn tự định nghĩa hệ thống token bằng ResourceDictionary thuần WPF (`src/LunarCal.App/Resources/Tokens.xaml`), cùng 3 theme (`Themes/Light.xaml`, `Dark.xaml`, `HighContrast.xaml`), quản lý qua `IThemeService`.
- XAML chỉ được tham chiếu token qua `{DynamicResource ...}` (để đổi theme khi đang chạy), không đặt màu hay cỡ chữ hard-code trong view.

### Bảng token đo từ Figma 01–05 (P2-00, Claude Code, 2026-11-01)

File Figma không có Variables, paint style hay text style nào (đã kiểm tra ngày 2026-11-01). Các giá trị dưới đây được đo trực tiếp từ node của frame 01–05 (đếm tần suất màu, font, radius, padding), và là **nguồn sự thật cho `Tokens.xaml` và `Themes/Light.xaml`**. Cột "Code hiện tại" ghi giá trị trong code lúc đo; chỗ nào khác Figma thì code phải sửa theo Figma.

Figma chỉ có theme Sáng. Theme Tối và Tương phản cao **chưa có thiết kế**: tạm giữ giá trị trong code, coi là đề xuất, chờ chủ dự án duyệt.

#### Chữ

| Key | Figma | Code hiện tại | Dùng cho |
| --- | --- | --- | --- |
| `FontFamilyBase` | Inter (toàn bộ frame) | Segoe UI | Xem ghi chú font bên dưới |
| `FontSizeCaption` | 11 (Regular/Medium/SemiBold) | 11 | Ngày âm, chip sự kiện, nhãn nhỏ |
| `FontSizeBody` | 12 (Regular) | 13 | Mô tả, nhãn phụ |
| `FontSizeBodyStrong` | 13 (Medium) | — | Số ngày dương, nút, mục danh sách |
| `FontSizeSubtitle` | 14 (Medium) | 15 | Tiêu đề hàng cài đặt, mục nav |
| `FontSizeSection` | 16 (SemiBold) | — | Tiêu đề nhóm cài đặt |
| `FontSizeTitle` | 20 (Bold) | 18 | Tiêu đề top bar ("Tháng 10, 2026") |
| `FontSizeDisplay` | 24 (SemiBold) | 24 | "Cài đặt" |
| Weight | 400, 500, 600, 700 | như Figma | |

Font: Figma dùng Inter, nhưng Inter không có sẵn trên Windows. Phải chọn giữa nhúng Inter vào app (license SIL OFL 1.1, cần ghi ADR) hoặc dùng Segoe UI Variable/Segoe UI và chấp nhận lệch so với Figma. **Chưa quyết**, xem câu hỏi mở trong `docs/STATUS.md`.

#### Khoảng cách, bo góc, kích thước

| Key | Figma | Code hiện tại |
| --- | --- | --- |
| Spacing | 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 28 (gap hay gặp nhất: 4, 8, 10) | 4, 8, 12, 16, 24, 32 |
| Padding ô ngày (lưới Tháng) | 8/8/6/8 (trên/phải/dưới/trái) | — |
| Padding chip sự kiện | 3/6 | — |
| Padding nút | 9/14 (nút thường), 6/14 (segmented) | 6/12 |
| Padding hàng cài đặt | 14/18 | — |
| `CornerRadiusSmall` | 5 (chip sự kiện) | 4 |
| `CornerRadiusMedium` | 8 (nút, mục nav), 7 (ô segmented), 9 (khung segmented) | 8 |
| `CornerRadiusLarge` | 12 (card cài đặt), 14 (card lớn, thanh lệnh AI) | 12 |
| `CornerRadiusDayNumber` | 13 (vòng tròn số ngày 26×26) | — |
| `CornerRadiusFull` | 999 (chip trạng thái, toggle) | 999 |
| `SidebarWidth` | 280 | 240 |
| `TopBarHeight` | 64 | 52 |
| `AiCommandBarHeight` | 92 (ô nhập 59, lề 24) | 48 |
| `WeekdayHeaderHeight` | 36 | — |
| Ô lưới Tháng | 165×115, kẻ lưới 1 px | — |
| `MiniCalendarCellSize` | 28 (hàng 28, giãn 34) | 28 |
| `DayDetailPanelWidth` | 360 (S07, S09) | — |
| Lưới giờ (S06, S07) | 48 px/giờ, cột giờ 56 | — |

#### Màu (theme Sáng)

| Key | Figma | Code hiện tại | Dùng cho |
| --- | --- | --- | --- |
| `WindowBackgroundBrush` | `#F6F7F9` | `#FFFFFF` | Nền cửa sổ, nền vùng nội dung Cài đặt |
| `SurfaceBackgroundBrush` | `#FFFFFF` | `#F8F9FA` | Sidebar, top bar, ô ngày, card |
| `SurfaceMutedBrush` | `#F9FAFB` / `#FAFAFB` | — | Khối trạng thái đồng bộ, ô ngoài tháng |
| `ControlTrackBrush` | `#F2F4F7` | — | Nền segmented, chip VI/EN |
| `BorderBrush` | `#EAECF0` | `#DADCE0` | Kẻ lưới, viền card |
| `BorderStrongBrush` | `#D0D5DD` | — | Viền nút phụ, ô nhập |
| `TextPrimaryBrush` | `#101828` | `#202124` | Chữ chính |
| `TextBodyBrush` | `#344054` | — | Nút phụ, nhãn sidebar |
| `TextSecondaryBrush` | `#667085` | `#5F6368` | Thứ trong tuần, mô tả |
| `TextTertiaryBrush` | `#98A2B3` | `#80868B` | Nhãn mục, số ngày ngoài tháng |
| `TextDisabledBrush` | `#D0D5DD` | — | Số âm ngoài tháng, placeholder |
| `AccentPrimaryBrush` | `#2F6BFF` | `#1A73E8` | Nút chính, hôm nay, toggle bật |
| `AccentSubtleBrush` | `#EAF1FF` (nav chọn: `#EDF2FF`) | `#E8F0FE` | Nền chip sự kiện xanh, mục nav đang chọn |
| `AccentTextBrush` | `#1D4ED8` | `#FFFFFF` (đang dùng làm chữ trên nền accent) | Chữ trên chip xanh. Chữ trên nền accent: `#FFFFFF` (`OnAccentBrush`) |
| `LunarTextBrush` | `#B54708` | `#D93025` | Số ngày âm, dòng "Âm lịch: …" |
| `LunarSubtleBrush` | `#FFF4E5` | — | Chip sự kiện âm lịch, khối âm lịch trong panel |
| `LunarStrongBrush` | `#93370D` | — | Chữ đậm trong khối âm lịch |
| `AiAccentBrush` | `#7A5AF8` | — | Viền thanh lệnh AI, nút gửi, lịch "Nhắc việc từ AI" |
| `AiSubtleBrush` | `#F1ECFF` / `#FAF8FF` | — | Chip sự kiện AI, nền ô nhập lệnh |
| `AiTextBrush` | `#5925DC` | — | Chữ chip AI, chip "VI · EN" |
| `SuccessBrush` | `#12B76A` (chấm), chữ `#067647`, nền `#ECFDF3` / `#E7F8EF` | `#0B8043` | Online, đã đồng bộ, lịch Công việc |
| `WarningBrush` | `#F79009`, chữ `#B54708`, nền `#FFFAEB` | `#F29900` | Offline, lịch Âm lịch cục bộ |
| `DangerBrush` | chữ `#B42318`, viền `#FDA29B`, nền `#FEF3F2` | `#D93025` | Ngắt kết nối, xoá dữ liệu |
| `NowLineBrush` | `#F04438` (S06, S07, đề xuất) | — | Vạch giờ hiện tại |
| `ScrimBrush` | `#101828` @ 45% | — | Nền mờ sau hộp thoại (02) |
| Màu calendar | `#2F6BFF`, `#12B76A`, `#F79009`, `#7A5AF8` | — | Cá nhân, Công việc, Âm lịch, AI |

Bóng: card nổi `#101828` @ 4%, offset (4,0), blur 16; drawer `#0F1729` @ 18%, offset (−8,0), blur 32; hộp thoại `#0F1729` @ 25%, offset (0,20), blur 50.

Tên key ở cột 1 là đề xuất để khớp Figma; key nào chưa có trong code thì thêm khi sửa `Tokens.xaml`.

### Token bổ sung khi bỏ giá trị hard-code (P2-R5, Claude Code, 2026-11-01)

Giữ nguyên giá trị đang hiển thị ở Phase 2, chỉ đặt tên. Khi Figma có số đo khác thì sửa giá trị ở `Tokens.xaml`, không sửa view.

| Key | Giá trị | Dùng cho |
| --- | --- | --- |
| `FontSizeBadge` | 10 | Badge số lệnh AI đang chờ (sidebar) |
| `FontSizeLunarHero` | 40 | Số ngày âm lớn ở panel chi tiết ngày (S09) |
| `IconSizeSmall` / `Medium` / `Large` / `XLarge` | 14 / 16 / 20 / 24 | Glyph biểu tượng (✕, ✨, 🌙, +, 📅) |
| `IconSizeEmptyState` | 36 | Biểu tượng trạng thái rỗng (Lịch biểu) |
| `CornerRadiusStripe` | 2 | Vạch màu calendar ở danh sách sự kiện |
| `CornerRadiusAiBar` | 24 | Thanh lệnh AI |
| `OnWarningBrush` | Sáng/Tối `#101828`, HC `WindowColor` | Chữ trên nền `WarningBrush` |

Hình tròn (chấm màu, ô mini-calendar, avatar, nút gửi) dùng `CornerRadiusFull`. Màu mặc định cho calendar không có màu: hằng `CalendarColors.DefaultHex` (= `AccentPrimaryBrush` sáng). Test `ThemeDictionaryTests.FR_UI_11_Views_and_controls_use_tokens_instead_of_literal_sizes_and_colors` chặn literal mới trong `Views/` và `Controls/`.

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
