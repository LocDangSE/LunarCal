# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-08 18:30 +07:00, Claude Code (Sonnet 5.5). P4-R8: "Sửa" điền sẵn đề xuất, huỷ giữa chừng không bị ghi đè, spec §6 thêm `proposal_json`; 804 test pass. Trước đó (17:16): Sửa bốn lỗi chủ dự án báo khi review Phase 4 (P4-R5..P4-R7, ADR-0032): giờ AI sai, chờ quá lâu, thiếu phản hồi khi xử lý, ngăn kéo không đóng. 796 test pass (Debug); chưa đo latency với Gemini thật, chưa push. Đã vẽ Figma frame 4.5 (node 57:380) cho thẻ tiến trình. Trước đó (13:50): review xong Phase 4. Review xong Phase 4: cổng `P4-REVIEW` `[x]`, `phase/4` đã merge vào `main`. 753 test xanh ban đầu nhưng chuỗi AI (gõ lệnh → model → thẻ xem trước → xác nhận → ghi lịch) chưa chạy được trong app thật; đã sửa trong task P4-R4 (đăng ký orchestrator và executor, lưu đề xuất của model bằng migration V4, dịch vụ xác nhận là đường ghi duy nhất, SEC-LOG-2, đọc đối số theo schema, dựng lại hộp thoại theo Figma 02). Chi tiết: `docs/reviews/PHASE4.md`, ADR-0031. Release: build 0 warning, format pass, 767 test pass, coverage Lunar 99.54%, Core 92.5%.

**Phiên trước (Antigravity):** 2026-11-08 13:15 +07:00. Hoàn thành toàn bộ Phase 4 (P4-00..P4-13, P4-R1..P4-R3) và chuyển cổng `P4-REVIEW` sang `[r]`.

## Phase và task

- **Phase hiện tại:** Phase 4 đã đóng (cổng `P4-REVIEW` `[x]`, đã merge vào `main`). Phase 5 chưa bắt đầu; task đầu tiên của Phase 5 là `P5-00` (👤 thiết kế S11, S15) hoặc `P5-01`.
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ branch. Đã review Phase 4, sửa P4-R4 và merge `phase/4` vào `main`; sau đó sửa P4-R5..P4-R7 trực tiếp trên `main` (chủ dự án cho phép). Không có việc dở.
  - **Antigravity:** Hoàn thành toàn bộ task Phase 4 (đã được review). Chưa nhận task Phase 5. Trước khi bắt đầu, đọc `docs/reviews/PHASE4.md` mục "Nên sửa sau" và ADR-0031 (đề xuất của model lưu ở `AiCommand.proposal_json`; ghi lịch chỉ qua `IAiCommandConfirmationService`).
- **Task tiếp theo đề xuất:**
  0. Chủ dự án: chạy lại một lệnh AI thật, đọc dòng log `Gemini call finished in ... ms` trong `%LOCALAPPDATA%\LunarCal\logs` để biết thinking `low` có đủ nhanh và đúng không; nếu vẫn chậm, quyết định đổi `DefaultModelId` (ADR-0032).
  1. Chủ dự án: push `main` và `phase/4` lên remote, xem CI trên GitHub; thử một lệnh AI thật với key Gemini của mình (gõ lệnh → mở ngăn kéo → Xác nhận) vì chưa có test nào gọi Gemini thật.
  2. Antigravity: bắt đầu Phase 5 (`P5-00` cần thiết kế Figma S11, S15 trước; `P5-01`, `P5-02` làm được ngay). `CalendarToolExecutor` mới hiện thực `create_event`; Phase 5 mở rộng cho các tool còn lại.

## Câu hỏi mở

- (Không còn câu hỏi mở. Cột `AiCommand.proposal_json` đã được ghi vào spec §6 ngày 2026-11-08, P4-R8.)

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | Hoàn thành 100% (P0-00, P0-01, P0-02, P0-03, P0-04); CodeQL nâng lên 4.39.1; CI xanh trên GitHub | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, `codeql.yml`, ADR-0001..0005, ADR-0023, `.gitleaks.toml`, SourceLink trong PDB |
| 1 | Xong: FR-LUNAR-1..3, coverage Lunar 100%, CI xanh | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2 | Hoàn thành 100%: P2-00 (Figma S06–S09, S13 và design token), P2-01 (resx, i18n), P2-02 (pseudo-locale), P2-03 (theme & tokens, ADR-0011), P2-04 (settings store), P2-05 (event model & sample store), P2-06 (MainWindow shell, TopBar, Sidebar, MiniCalendar), P2-06b (cân chỉnh tokens theo Figma), P2-07 (MonthView 6×7 grid, lunar formatting, render < 100 ms), P2-08 (WeekView, DayView, timed event grid, now line, lane clustering), P2-09 (AgendaView, date grouping, calendar filters), P2-10 (DayDetailPanel, Can Chi, solar terms, glosses), P2-11 (AppearancePage, week start, 12h/24h, timezone), P2-12 (FlaUI UI smoke tests out-of-process), P2-13 (kết thúc Phase 2, cập nhật tài liệu, kiểm tra gates) | `tests/LunarCal.App.UiTests/SmokeTests.cs`, `src/LunarCal.App/Views/Settings/AppearancePage.xaml`, `DayDetailPanel.xaml`, `AgendaView.xaml`, `WeekView.xaml`, `DayView.xaml`, `MonthView.xaml`, `MainWindow.xaml`, `tests/`, `docs/TRACEABILITY.md`, `CHANGELOG.md` |
| 3 | Hoàn thành 100% (P3-00a..P3-16, P3-R1..P3-R4; đã được Claude Code review và merge vào main) | `docs/reviews/PHASE3.md`, commit merge trên `main`, ADR-0013, ADR-0017, ADR-0018, ADR-0019, ADR-0026, ADR-0028 |
| 4 | Hoàn thành 100% (P4-00..P4-13, P4-R1..P4-R4); đã được Claude Code review và merge vào `main` | `docs/reviews/PHASE4.md`, ADR-0031, migration V4, `AiCommandConfirmationService`, `CalendarToolExecutor`, 767 tests pass, coverage Lunar 99.54%, Core 92.5% |
| 5–8 | Chưa bắt đầu (cổng P4-REVIEW đã `[x]`) | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): `release.yml` (P8-02). Các mục gitleaks, CodeQL, SourceLink đều đã hoàn tất.

## Build, test, format (chạy ngày 2026-11-08, Windows 11, .NET SDK 10.0.403, Release)

Lần chạy mới nhất 2026-11-08 13:50 (Release, `phase/4` trước khi merge): build 0 warning, format pass, 767 test pass (Core, Infra, App, 5 UiTests), 0 skip. Coverage gate: Lunar 99.54%, Core 92.5% (ngưỡng 95% và 85%). Lưu ý: `MonthViewRenderTests.NFR_PERF_*` đạt sát ngưỡng 100 ms và có thể fail khi chạy cùng `--coverage` trên máy đang tải; chạy riêng thì xanh.

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx -c Release` | 767 test pass |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 99.54%, Core 92.5% |

- **Số đo render MonthView (P2-07, NFR-PERF < 100 ms):** test `MonthViewRenderTests.NFR_PERF_Month_view_renders_in_under_100_ms_with_200_events` dựng `MonthView` thật trên luồng STA với `Tokens.xaml` + `Light.xaml`, 200 sự kiện/tháng. Sau khi tối ưu hóa binding và tái sử dụng chip slots: warm min 69.9 ms, median 96.1 ms (đạt ngân sách < 100 ms).
- **Kiểm tra đổi theme khi đang chạy (P2-03):** smoke test `Settings_button_opens_appearance_page`: mở Cài đặt > Giao diện, chọn lần lượt Tối, Tương phản cao, Sáng, Theo hệ thống; app không thoát, radio được chọn đúng. Pass.

## Quyết định đã chốt mà spec chưa ghi

- ADR-0027 (2026-11-02): Claude Code review theo phase thay vì từng task. Task `[x]` = đã merge vào `phase/<N>`, chưa review. Cổng `P<N>-REVIEW` phải `[x]` (đã review, đã merge vào `main`) thì mới được sang phase sau.
- ADR-0029 (2026-11-07): Một tài khoản Google tại một thời điểm. Đổi tài khoản hiển thị xác nhận và xoá dữ liệu cũ.
- ADR-0030 (2026-11-08): System prompt AI đặt tại Core EmbeddedResource `Prompts/system.v1.md`.
- ADR-0031 (2026-11-08): Đề xuất của model lưu ở `AiCommand.proposal_json`; ghi lịch chỉ qua `IAiCommandConfirmationService`.
- ADR-0032 (2026-11-08): Giờ lệnh AI theo múi giờ người dùng, thinking thấp, thử lại nhanh, thẻ tiến trình không chặn.

Chi tiết nằm trong ADR (`docs/DECISIONS.md`).
