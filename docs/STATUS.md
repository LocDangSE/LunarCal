# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-01 13:45 +07:00, Antigravity. Hoàn thành P2-08 (View Tuần `WeekView.xaml`, Ngày `DayView.xaml`, `WeekViewModel`, `DayViewModel`, `DayColumnViewModel`, `TimedEventViewModel`, converters `LaneMarginConverter`, `LaneWidthConverter`, `LanePositionConverter`: lưới giờ 24h 48 px/h, cột giờ 56 px, hàng cả ngày, vạch giờ hiện tại `NowLineBrush`, ngày âm tiêu đề cột, thuật toán chia làn thông minh khi trùng giờ, tích hợp MainWindow). 158 test (157 pass, 1 skip).

**Trước đó:** 2026-11-01 12:40 +07:00, Claude Code (Opus 5.5). Hỗ trợ P2-00 (task 👤): đo token từ Figma 01–05 vào `.agents/rules/ui.md`, vẽ nháp S06–S09 và S13 trong Figma. Không sửa code.

## Phase và task

- **Phase hiện tại:** Phase 0 hoàn thành 100%. Phase 1 xong, đã xanh trên CI. Phase 2 đang thực hiện (P2-00, P2-01, P2-02, P2-03, P2-04, P2-05, P2-06, P2-06b, P2-07, P2-08 đã xong; tiếp theo: P2-09).
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ task nào. Phiên 2026-11-01 12:40: hỗ trợ chủ dự án làm P2-00 (task 👤, vẫn `[!]`). (1) Đo token từ node Figma 01–05 và ghi bảng mới vào `.agents/rules/ui.md` mục "Design token". (2) Vẽ nháp trong file Figma `4vwWjHGoSu9naj5ou86QYs`, hàng y=2300: S06 Tuần `23:2`, S07 Ngày `23:493`, S08 Lịch biểu `23:984`, S09 Chi tiết ngày `23:1475` và bảng trạng thái `26:127`, S13 Cài đặt > Giao diện `23:1966`; component `Day detail panel` `25:2`, component set `Day cell` `26:126`. Logo trong các frame mới đổi thành "LunarCal" (frame 01–05 vẫn ghi "GCalendar"). Bước tiếp theo: chủ dự án duyệt hoặc sửa các frame. Khi đã duyệt thì đánh dấu P2-00 `[x]`, đóng Q-002 và tạo task sửa `Tokens.xaml`/`Themes/Light.xaml` cho khớp Figma (xem Q-005). File đang sửa dở: không có. Chưa commit.
  - **Antigravity:** không giữ task nào. Vừa hoàn thành P2-08 (View Tuần S06 và Ngày S07: lưới giờ 24h, cột giờ 56 px, hàng cả ngày, vạch giờ hiện tại, ngày âm ở tiêu đề cột, thuật toán chia làn sự kiện trùng giờ, unit tests). Tiếp theo: P2-09 (Lịch biểu AgendaView: danh sách nhóm theo ngày, có ngày âm, lọc theo calendar).
- **Task tiếp theo đề xuất:**
  1. Phase 2 Agenda view: P2-09 (Lịch biểu: danh sách nhóm theo ngày, có ngày âm, lọc theo calendar).
  2. Phase 2 Day detail panel: P2-10 (Panel chi tiết ngày: can chi ngày/tháng/năm, tiết khí, ký hiệu nhuận, danh sách sự kiện).
  3. Phase 2 Cài đặt giao diện: P2-11 (Đầu tuần cấu hình được, 12h/24h, định dạng theo culture, chọn múi giờ khác múi hệ thống).

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | Hoàn thành 100% (P0-00, P0-01, P0-02, P0-03, P0-04); CodeQL nâng lên 4.39.1; CI xanh trên GitHub | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, `codeql.yml`, ADR-0001..0005, ADR-0023, `.gitleaks.toml`, SourceLink trong PDB |
| 1 | Xong: FR-LUNAR-1..3, coverage Lunar 100%, CI xanh | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2 | Đang thực hiện: P2-00 (Figma S06–S09, S13 và design token), P2-01 (resx, i18n), P2-02 (pseudo-locale), P2-03 (theme & tokens, ADR-0011), P2-04 (settings store), P2-05 (event model & sample store), P2-06 (MainWindow shell, TopBar, Sidebar, MiniCalendar), P2-06b (cân chỉnh tokens theo Figma), P2-07 (MonthView 6×7 grid, lunar formatting, render < 100 ms), P2-08 (WeekView, DayView, timed event grid, now line, lane clustering) xong | `src/LunarCal.App/Views/WeekView.xaml`, `DayView.xaml`, `MonthView.xaml`, `MainWindow.xaml`, `src/LunarCal.App/ViewModels/WeekViewModel.cs`, `DayViewModel.cs`, `tests/` |
| 3–8 | Chưa bắt đầu | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): `release.yml` (P8-02). Các mục gitleaks, CodeQL, SourceLink đều đã hoàn tất.

## Build, test, format (chạy ngày 2026-11-01, Windows 11, .NET SDK 10.0.403)

| Lệnh | Kết quả |
| --- | --- |
| `dotnet restore LunarCal.slnx --locked-mode` | Pass |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx` | 158 test: 157 pass, 0 fail, 1 skip |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 100%, Core 99.67% (ngưỡng 95% và 85%) |
| `dotnet run --project src/LunarCal.App` | Cửa sổ "LunarCal" mở được với UI shell TopBar, Sidebar, MiniCalendar và chuyển đổi mượt mà giữa Tháng, Tuần, Ngày |
| `docker build --target test .` (Linux, SDK 10.0.403-noble) | Pass: Core.Tests 69/69 (run trên CI commit 159781c) |

- Test skip: `LunarCal.App.UiTests.SmokeTests.App_starts_and_shows_main_window`, lý do "UI shell is implemented in Phase 2". Bỏ skip ở P2-12.
- CI đầu tiên (commit f418386) đỏ ở job `core-linux-container`: `FR_LUNAR_2_A_month_grid_is_computed_in_under_5_ms` đo 7–9 ms (tái hiện bằng `docker run --cpus=2`). Nguyên nhân: `.Should().HaveCount(42)` nằm trong vùng bấm giờ, nên test đo cả chi phí lần gọi đầu của AwesomeAssertions (reflection, tìm tên biến). Engine thật chỉ mất 0,02–0,3 ms mỗi tháng. Đã chuyển assertion ra sau `stopwatch.Stop()` ở cả hai test hiệu năng, giữ nguyên ngưỡng 5 ms. Sau khi sửa: 9/9 lần pass với `--cpus` 0.5, 1 và 2; commit 159781c xanh trên GitHub Actions run 41267053678.

## Quyết định đã chốt mà spec chưa ghi

Chi tiết nằm trong ADR (`docs/DECISIONS.md`):

- ADR-0003 AwesomeAssertions thay FluentAssertions (license). ADR-0004 xUnit v3 trên Microsoft.Testing.Platform. ADR-0005 `.slnx`.
- ADR-0006 lịch âm: phạm vi dương lịch 1900-01-01..2100-12-31, năm âm 1899..2100; ngoài phạm vi thì ném `ArgumentOutOfRangeException`; ngày âm không tồn tại thì `ToSolar` trả `null`; tháng nhuận dùng can chi của tháng thường; UTC+7 cho cả những năm trước 1968.
- ADR-0007 tiết khí được gán cho ngày (UTC+7) mà Mặt Trời đạt kinh độ; sai số tối đa khoảng 25 phút quanh nửa đêm.
- ADR-0008 tên can chi và tiết khí tiếng Việt nằm trong Core; chú giải tiếng Anh nằm trong resx (Phase 2).
- ADR-0009 bảng HKO 1901–2100 làm nguồn đối chiếu (trang Hồ Ngọc Đức trả HTTP 404).
- ADR-0010: WPF + MVVM + Generic Host. ADR-0011: Thư viện style WPF chốt chọn tự style bằng ResourceDictionary thuần WPF (`Tokens.xaml`, `Themes/Light.xaml`, `Dark.xaml`, `HighContrast.xaml`), không thêm dependency bên ngoài, đổi theme thời gian thực qua `IThemeService`.
- ADR-0018 dùng DPAPI, không dùng Credential Manager. ADR-0020 hồ sơ bàn giao. ADR-0021 workflow Antigravity gọi qua skill (`.agents/skills/`), vì `.agents/workflows/` không còn được index sau 19/10/2026.
- ADR-0022 đồng bộ tiến độ giữa Claude Code và Antigravity: người giữ task ghi trong TASKS.md, log chỉ ghi nối tiếp ở `docs/PROGRESS_LOG.md`, STATUS có mục riêng cho từng agent, commit có trailer `Agent:`.
- `.editorconfig`: hằng số đặt PascalCase, field private đặt `_camelCase`.
- ADR-0023: tên dự án **LunarCal** đã chốt (chủ dự án, 2026-11-01); nhánh `main`, remote `LocDangSE/LunarCal`; `Dockerfile` Linux chỉ build/test Core, image ghim digest; CI có job `core-linux-container`. Spec dòng 3 vẫn ghi "LunarCal là tên tạm". Agent không tự sửa spec, chủ dự án cập nhật khi cần.
- ADR-0024: Hạ tầng song ngữ i18n và quy ước đặt key (`area.element.variant` cho UI, `Toast_*` cho thông báo, `Strings.resx` làm neutral fallback chứa tiếng Việt cho `ResourceManager`, `LocalizationService` live switching qua `INotifyPropertyChanged`).

## Câu hỏi mở

### Q-001: Câu hỏi bắt buộc cho chủ dự án (spec §14)

- **Mục spec:** §14
- **Loại:** Thiếu đặc tả (thuộc danh sách được phép hỏi)
- **Mô tả:** Chưa có câu trả lời cho: (1) biểu tượng, nhà phát hành (tên app đã chốt là LunarCal ngày 2026-11-01, ADR-0023); (2) OAuth Client ID và trạng thái consent screen; (3) chứng chỉ ký mã và kênh cập nhật; (4) mặc định có tự khởi động cùng Windows không; (5) nhà cung cấp, vùng và ngân sách cloud; (6) bên kiểm soát dữ liệu, liên hệ quyền riêng tư, độ tuổi tối thiểu; (7) xác nhận hai quyết định ở §15.3 (key Gemini chỉ ở máy người dùng; lịch âm mặc định lưu cục bộ); (8) bản tiếng Anh cho các chuỗi giao diện còn lại.
- **Ảnh hưởng:** P3-00, P6b-00, P7-00, P8-00; các task resx từ P2-01.
- **Đề xuất / tạm quyết:** Code hiện theo spec (key ở máy, lịch âm cục bộ). Không chặn Phase 2 trừ câu (8), với câu này tạm dùng bản dịch nháp.
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-002: Thiết kế Figma cho Phase 2 (Đã đóng)

- **Mục spec:** §13 (Điều chỉnh lộ trình v1.1), §15.4 (S06, S07, S08, S09, S13)
- **Loại:** Thiếu đặc tả
- **Mô tả:** Spec yêu cầu thiết kế xong màn hình của một phase trước khi bắt đầu phase đó. File Figma `4vwWjHGoSu9naj5ou86QYs` ban đầu chỉ có 5 frame 01–05.
- **Cập nhật 2026-11-01 12:48 (Antigravity):** Chủ dự án đã cập nhật và duyệt các frame S06 (Tuần `23:2`), S07 (Ngày `23:493`), S08 (Lịch biểu `23:984`), S09 (Chi tiết ngày `23:1475` & `26:127`), S13 (Cài đặt > Giao diện `23:1966`) trong Figma. Bảng design token đã đo từ 01–05 và ghi vào `.agents/rules/ui.md`. Task P2-00 hoàn tất.
- **Trạng thái:** Đã đóng
- **Ghi bởi:** Claude Code, 2026-10-31 · Đóng ngày 2026-11-01

### Q-003: Bốn năm nhuận chưa có nguồn đối chiếu thứ hai

- **Mục spec:** §4.4, FR-LUNAR-3
- **Loại:** Mơ hồ (độ tin cậy của dữ liệu)
- **Mô tả:** Tháng nhuận của Việt Nam các năm 1917, 1922, 1938, 1987 khác HKO. Kết quả dựa trên thuật toán cộng lời giải thích lệch múi giờ, chưa có bảng âm lịch Việt Nam in sẵn để đối chiếu.
- **Ảnh hưởng:** Không chặn task nào.
- **Đề xuất / tạm quyết:** Giữ kết quả hiện tại (ADR-0009). Nếu có nguồn, thêm vào `tests/LunarCal.Core.Tests/Fixtures/`.
- **Trạng thái:** Tạm quyết
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-004: Quyền phân phối lại dữ liệu HKO trong repo công khai

- **Mục spec:** FR-LUNAR-3
- **Loại:** Mơ hồ
- **Mô tả:** `tests/LunarCal.Core.Tests/Fixtures/hko-*.csv` lấy từ bảng của Hong Kong Observatory (cũng đăng trên DATA.GOV.HK). Điều khoản sử dụng cho việc phân phối lại là CHƯA XÁC MINH.
- **Ảnh hưởng:** P0-01, nếu repo để công khai.
- **Đề xuất / tạm quyết:** Chủ dự án đọc điều khoản trước khi công khai repo. Đã ghi nguồn trong `Fixtures/README.md`.
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-005: Token trong code lệch khỏi Figma; chọn font (Đã đóng)

- **Mục spec:** §15.4, FR-UI-7, FR-UI-11; ADR-0011
- **Loại:** Mâu thuẫn (giữa code và thiết kế)
- **Mô tả:** `Tokens.xaml` và `Themes/Light.xaml` ban đầu dùng bảng màu Google Material (`#1A73E8`, `#202124`, ...), sidebar 240, top bar 52, thanh AI 48. Figma 01–05 dùng `#2F6BFF`, thang xám `#101828`…`#F9FAFB`, màu âm lịch `#B54708`, font Inter fallback Segoe UI, sidebar 280, top bar 64, thanh AI 92.
- **Cập nhật 2026-11-01 13:02 (Antigravity):** Đã hoàn thành task P2-06b cân chỉnh toàn bộ `Tokens.xaml` và 3 theme `Light.xaml`, `Dark.xaml`, `HighContrast.xaml` theo đúng bảng đo Figma trong `ui.md`. Kiểm tra 144 unit tests xanh.
- **Trạng thái:** Đã đóng
- **Ghi bởi:** Claude Code, 2026-11-01 · Đóng ngày 2026-11-01
