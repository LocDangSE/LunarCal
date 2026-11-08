# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-08 13:15 +07:00, Antigravity. Hoàn thành toàn bộ Phase 4 (P4-00..P4-13, P4-R1..P4-R3). Cổng `P4-REVIEW` đã chuyển sang `[r]`. Rà soát hoàn tất Phase 4 DoD (spec §13): BYOK, Gemini client, ChatOrchestrator state machine, hàng chờ lệnh SQLite AiCommand & worker hosted service, thanh lệnh AiCommandBar & ngăn kéo hàng chờ AiQueueDrawer, hộp thoại xác nhận AiConfirmDialog với đánh giá độ tin cậy và kiểm tra nghiệp vụ/xung đột lịch, trạng thái hỏi lại S16 với gợi ý trả lời nhanh, phân loại và ánh xạ lỗi Gemini với điều hướng Settings > AI, bảo vệ khoá DPAPI và chống lộ key, một tài khoản Google (ADR-0029), khoá chính phức hợp Event (calendar_id, id), và nâng cấp trình soạn sự kiện S10. Toàn bộ 753/753 test pass trong Release mode (177 Core, 337 Infra, 234 App, 5 UiTests), format verified, coverage gate đạt: Lunar 99.54% (ngưỡng 95%), Core 92.16% (ngưỡng 85%), không có package lỗi thời hay lỗ hổng.

**Phiên trước:** 2026-11-08 12:55 +07:00, Antigravity. Hoàn thành P4-12 (FR-CHAT-7: Xử lý và ánh xạ lỗi Gemini: `AiChatErrorKind`, `AiChatErrorAction`, `AiChatErrorResult`, `IAiChatErrorMapper` và `AiChatErrorMapper`; Key sai/thiếu/lộ/billing/model not found → mở Settings > AI, không retry vô ích; 429 rate limit → cooldown 30s & backoff; 429 quota ngày → thông báo giờ đặt lại lúc nửa đêm giờ Pacific; bị chặn an toàn → thông báo rõ ràng bộ lọc an toàn; mất kết nối mạng → bảo tồn lệnh ở SQLite queue; che key theo SEC-KEY-3; nút "Mở Cài đặt AI" trong drawer). Merge vào `phase/4`.

**Trước đó:** 2026-11-08 10:30 +07:00, Antigravity. Hoàn thành P4-11 (FR-CONFIRM-8: Trạng thái hỏi lại S16 `46:370`, dialog `46:862` khi model cần thêm thông tin). Toàn bộ 724 test pass, coverage Core 92.12%, Lunar 99.54%. Merge vào `phase/4`.

## Phase và task

- **Phase hiện tại:** Phase 4, nhánh `phase/4`. Mọi task `P4-00`..`P4-13`, `P4-R1`..`P4-R3` đều đã `[x]`. Cổng review `P4-REVIEW` đang ở trạng thái `[r]` (sẵn sàng chờ review).
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ branch. P4-R2 xong và đã merge vào `phase/4`. Review Phase 4 khi cổng `P4-REVIEW` ở `[r]`.
  - **Antigravity:** Hoàn thành toàn bộ các task của Phase 4, đã merge tất cả vào nhánh `phase/4`. Đã hoàn tất P4-13 và chuyển cổng `P4-REVIEW` sang `[r]`. Dừng làm việc trên code Phase 4, chờ Claude Code review theo ADR-0027 và `.agents/rules/progress-sync.md`.
- **Task tiếp theo đề xuất:**
  1. Claude Code: Tiếp nhận review Phase 4 (`P4-REVIEW`: `[r]` → `[f]`), rà soát `git diff main...phase/4`, chạy build/test/format, sửa bug (nếu có) kèm test tái hiện, lập tài liệu `docs/reviews/PHASE4.md`, chuyển cổng `P4-REVIEW` sang `[x]` và merge `phase/4` vào `main`.
  2. Chủ dự án: push `phase/4` lên remote và quan sát pipeline CI trên GitHub.

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | Hoàn thành 100% (P0-00, P0-01, P0-02, P0-03, P0-04); CodeQL nâng lên 4.39.1; CI xanh trên GitHub | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, `codeql.yml`, ADR-0001..0005, ADR-0023, `.gitleaks.toml`, SourceLink trong PDB |
| 1 | Xong: FR-LUNAR-1..3, coverage Lunar 100%, CI xanh | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2 | Hoàn thành 100%: P2-00 (Figma S06–S09, S13 và design token), P2-01 (resx, i18n), P2-02 (pseudo-locale), P2-03 (theme & tokens, ADR-0011), P2-04 (settings store), P2-05 (event model & sample store), P2-06 (MainWindow shell, TopBar, Sidebar, MiniCalendar), P2-06b (cân chỉnh tokens theo Figma), P2-07 (MonthView 6×7 grid, lunar formatting, render < 100 ms), P2-08 (WeekView, DayView, timed event grid, now line, lane clustering), P2-09 (AgendaView, date grouping, calendar filters), P2-10 (DayDetailPanel, Can Chi, solar terms, glosses), P2-11 (AppearancePage, week start, 12h/24h, timezone), P2-12 (FlaUI UI smoke tests out-of-process), P2-13 (kết thúc Phase 2, cập nhật tài liệu, kiểm tra gates) | `tests/LunarCal.App.UiTests/SmokeTests.cs`, `src/LunarCal.App/Views/Settings/AppearancePage.xaml`, `DayDetailPanel.xaml`, `AgendaView.xaml`, `WeekView.xaml`, `DayView.xaml`, `MonthView.xaml`, `MainWindow.xaml`, `tests/`, `docs/TRACEABILITY.md`, `CHANGELOG.md` |
| 3 | Hoàn thành 100% (P3-00a..P3-16, P3-R1..P3-R4; đã được Claude Code review và merge vào main) | `docs/reviews/PHASE3.md`, commit merge trên `main`, ADR-0013, ADR-0017, ADR-0018, ADR-0019, ADR-0026, ADR-0028 |
| 4 | Hoàn thành toàn bộ task (P4-00..P4-13, P4-R1..P4-R3) trên `phase/4`. Cổng `P4-REVIEW` ở `[r]`, chờ Claude Code review theo ADR-0027 | `phase/4`, `src/LunarCal.Core/`, `src/LunarCal.Infrastructure/`, `src/LunarCal.App/`, 753 tests pass, coverage Lunar 99.54%, Core 92.16% |
| 5–8 | Chưa bắt đầu (chờ cổng P4-REVIEW đạt [x]) | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): `release.yml` (P8-02). Các mục gitleaks, CodeQL, SourceLink đều đã hoàn tất.

## Build, test, format (chạy ngày 2026-11-08, Windows 11, .NET SDK 10.0.403, Release)

Lần chạy mới nhất 2026-11-08 13:15 (Release, `phase/4`): build 0 warning, format pass, 753 test (177 Core, 337 Infra, 234 App, 5 UiTests): 753 pass, 0 fail, 0 skip. Coverage gate: Lunar 99.54%, Core 92.16% (ngưỡng 95% và 85%). Zero vulnerable NuGet packages.

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx --no-build -c Release --coverage` | 753 test: 753 pass, 0 fail, 0 skip (2026-11-08 13:11) |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 99.54%, Core 92.16% (ngưỡng 95% và 85%; đo 2026-11-08 13:11) |
| UI smoke (`tests/LunarCal.App.UiTests`, 5 test) | Pass 5/5 |
| `dotnet list LunarCal.slnx package --vulnerable --include-transitive` | 0 vulnerable package |

- **Số đo render MonthView (P2-07, NFR-PERF < 100 ms):** test `MonthViewRenderTests.NFR_PERF_Month_view_renders_in_under_100_ms_with_200_events` dựng `MonthView` thật trên luồng STA với `Tokens.xaml` + `Light.xaml`, 200 sự kiện/tháng. Sau khi tối ưu hóa binding và tái sử dụng chip slots: warm min 69.9 ms, median 96.1 ms (đạt ngân sách < 100 ms).
- **Kiểm tra đổi theme khi đang chạy (P2-03):** smoke test `Settings_button_opens_appearance_page`: mở Cài đặt > Giao diện, chọn lần lượt Tối, Tương phản cao, Sáng, Theo hệ thống; app không thoát, radio được chọn đúng. Pass.

## Quyết định đã chốt mà spec chưa ghi

- ADR-0027 (2026-11-02): Claude Code review theo phase thay vì từng task. Task `[x]` = đã merge vào `phase/<N>`, chưa review. Cổng `P<N>-REVIEW` phải `[x]` (đã review, đã merge vào `main`) thì mới được sang phase sau.
- ADR-0029 (2026-11-07): Một tài khoản Google tại một thời điểm. Đổi tài khoản hiển thị xác nhận và xoá dữ liệu cũ.
- ADR-0030 (2026-11-08): System prompt AI đặt tại Core EmbeddedResource `Prompts/system.v1.md`.

Chi tiết nằm trong ADR (`docs/DECISIONS.md`).
