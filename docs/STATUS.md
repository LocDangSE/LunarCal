# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-08 12:55 +07:00, Antigravity. Hoàn thành P4-12 (FR-CHAT-7: Xử lý và ánh xạ lỗi Gemini: `AiChatErrorKind`, `AiChatErrorAction`, `AiChatErrorResult`, `IAiChatErrorMapper` và `AiChatErrorMapper`; Key sai/thiếu/lộ/billing/model not found → mở Settings > AI, không retry vô ích; 429 rate limit → cooldown 30s & backoff; 429 quota ngày → thông báo giờ đặt lại lúc nửa đêm giờ Pacific, không retry; bị chặn an toàn → thông báo rõ ràng bộ lọc an toàn, không retry; mất kết nối mạng → bảo tồn lệnh ở SQLite queue, tự động xử lý tiếp khi có mạng; che key theo SEC-KEY-3; nút "Mở Cài đặt AI" trong drawer khi gặp lỗi key). Toàn bộ 753 test pass (177 Core, 337 Infra, 234 App, 5 UiTests), coverage Core 91.59%, Lunar 99.54%.

**Phiên trước:** 2026-11-08 10:30 +07:00, Antigravity. Hoàn thành P4-11 (FR-CONFIRM-8: Trạng thái hỏi lại S16 `46:370`, dialog `46:862` khi model cần thêm thông tin). Toàn bộ 724 test pass (177 Core, 316 Infra, 226 App, 5 UiTests), coverage Core 92.12%, Lunar 99.54%. Merge vào `phase/4`.

**Phiên trước:** 2026-11-08 10:00 +07:00, Antigravity. Hoàn thành P4-10 (Hộp thoại xác nhận AI `Views/AiConfirmDialog` Figma 02 `2:2`/`2:494`, heuristic độ tin cậy `AiConfidenceEvaluator`, validator nghiệp vụ cục bộ trên SQLite cache `IAiBusinessValidator`/`AiBusinessValidator`, khoá nút Xác nhận khi độ tin cậy < 60% hoặc validator có lỗi, nút Sửa mở EventEditorDialog, ghi chú opt-in). Merge commit daa398d trên phase/4.

**Trước đó:** 2026-11-08 09:18 +07:00, Antigravity. Hoàn thành P4-09 (Thanh lệnh `Controls/AiCommandBar` Figma 01, chip ngôn ngữ vi/en/mixed, banner offline; ngăn kéo hàng chờ `Views/AiQueueDrawer` Figma 03). Merge commit 3e681de trên phase/4.

## Phase và task

- **Phase hiện tại:** Phase 4, nhánh `phase/4`. `P4-00`, `P4-01`, `P4-02`, `P4-03`, `P4-04`, `P4-05`, `P4-06`, `P4-07`, `P4-08`, `P4-09`, `P4-10`, `P4-11`, `P4-12`, `P4-R1`, `P4-R2`, `P4-R3` `[x]`. Sẵn sàng làm `P4-13` (spec §13: Kết thúc Phase 4: TRACEABILITY, commit phase; CI xanh).
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ branch. P4-R2 xong và đã merge vào `phase/4`. Review Phase 4 khi cổng `P4-REVIEW` ở `[r]`.
  - **Antigravity:** Hoàn thành P4-12 trên branch `task/P4-12`, merge vào `phase/4`. Tiếp theo nhận P4-13 (Kết thúc Phase 4). Toàn bộ 753 test pass, format sạch, coverage gate đạt (Lunar 99.54%, Core 91.59%).
    - Đọc trước khi làm Phase 4 (spec đã cập nhật 2026-11-07): ADR-0016 (Gemini, bảo vệ key), ADR-0029 (một tài khoản), ADR-0030 (vị trí prompt), spec §4.5 (FR-BYOK-1..7), §5.1 (SEC-KEY-6..11), §7 (Tích hợp Gemini), FR-CONFIRM-8, FR-QUEUE-2 (mã trạng thái `AiCommand`: `pending`, `processing`, `needs_confirmation`, `error`, `completed`, `cancelled`) và §13. Thiết kế đã duyệt, node id ở `.agents/rules/ui.md`: Figma 01 (`1:2`), Figma 02 (`2:2`, `2:494`), Figma 03 (`3:2`, `3:493`), S14 1.3, S16 4.4, S18 7.1, S20 5.3.
- **Task tiếp theo đề xuất:**
  1. Antigravity: Bắt đầu P4-13 (spec §13: Kết thúc Phase 4: TRACEABILITY, commit phase; chuyển cổng P4-REVIEW sang [r]).
  2. Chủ dự án: push `phase/4`, `main` và `task/P4-R2` rồi xem CI trên GitHub (tab Actions).
  3. Chủ dự án: kiểm tra thủ công đăng nhập tài khoản Google khác trên app thật: hộp thoại hiện, huỷ giữ nguyên dữ liệu, đồng ý thì lịch mới được tải về.

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | Hoàn thành 100% (P0-00, P0-01, P0-02, P0-03, P0-04); CodeQL nâng lên 4.39.1; CI xanh trên GitHub | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, `codeql.yml`, ADR-0001..0005, ADR-0023, `.gitleaks.toml`, SourceLink trong PDB |
| 1 | Xong: FR-LUNAR-1..3, coverage Lunar 100%, CI xanh | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2 | Hoàn thành 100%: P2-00 (Figma S06–S09, S13 và design token), P2-01 (resx, i18n), P2-02 (pseudo-locale), P2-03 (theme & tokens, ADR-0011), P2-04 (settings store), P2-05 (event model & sample store), P2-06 (MainWindow shell, TopBar, Sidebar, MiniCalendar), P2-06b (cân chỉnh tokens theo Figma), P2-07 (MonthView 6×7 grid, lunar formatting, render < 100 ms), P2-08 (WeekView, DayView, timed event grid, now line, lane clustering), P2-09 (AgendaView, date grouping, calendar filters), P2-10 (DayDetailPanel, Can Chi, solar terms, glosses), P2-11 (AppearancePage, week start, 12h/24h, timezone), P2-12 (FlaUI UI smoke tests out-of-process), P2-13 (kết thúc Phase 2, cập nhật tài liệu, kiểm tra gates) | `tests/LunarCal.App.UiTests/SmokeTests.cs`, `src/LunarCal.App/Views/Settings/AppearancePage.xaml`, `DayDetailPanel.xaml`, `AgendaView.xaml`, `WeekView.xaml`, `DayView.xaml`, `MonthView.xaml`, `MainWindow.xaml`, `tests/`, `docs/TRACEABILITY.md`, `CHANGELOG.md` |
| 3 | Hoàn thành 100% (P3-00a..P3-16): P3-00a xong (Client ID trong user-secrets, consent Testing); P3-00b xong (Figma S10, S14, S18, S20); P3-01 xong (Serilog file sink xoay vòng 14 ngày, LogRedactor che bí mật, SEC-LOG-1, SEC-LOG-2); P3-02 xong (ISecretStore, DpapiSecretStore, SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3, ADR-0018); P3-03 xong (SQLCipher encrypted storage, migration runner V1, 9 bảng spec §6, index, SEC-DATA-1, ADR-0013); P3-04 xong (ACL thư mục SEC-DATA-2, LocalDataWipeService SEC-DATA-3); P3-05 xong (Google OAuth desktop PKCE S256, loopback 127.0.0.1, state, timeout 5 phút, scope tối thiểu, ADR-0017 Accepted, FR-AUTH-1, FR-AUTH-2, FR-AUTH-6, SEC-OAUTH-1, SEC-OAUTH-2); P3-06 xong (Tự động refresh token, invalid_grant đăng nhập lại, hướng dẫn consent Testing hết hạn sau 7 ngày, FR-AUTH-3, FR-AUTH-6); P3-07 xong (Đăng xuất Google account: revoke endpoint, xoá refresh token DPAPI, purge toàn bộ cache Account/CalendarInfo/Event/ReminderState/LunarRule khỏi SQLite có xác nhận, FR-AUTH-4); P3-08 xong (HttpClient resilience pipeline, allow-list host, HTTPS only, timeout, circuit breaker, retry idempotency filter, SEC-NET-1, SEC-NET-2); P3-09 xong (Google Calendar API v3 Gateway và SyncEngine: đồng bộ lần đầu calendar list và events [-12m, +24m], singleEvents=true, SQLite persistence, extended_json, FR-SYNC-1, FR-SYNC-6, ADR-0028); P3-10 xong (Incremental sync bằng syncToken, xử lý sự kiện bị huỷ, upsert, tự động kích hoạt full resync khi 410 Gone, FR-SYNC-2); P3-11 xong (Quota management và throttling <= 5 req/s via IClock, exponential backoff và jitter cho 429 và 403 rateLimitExceeded, FR-SYNC-5); P3-12 xong (Hosted background sync scheduler, polling thích ứng 2m active / 10m tray, trigger tức thời focus / post-mutation / thủ công, coalescing, FR-SYNC-3); P3-13 xong (PendingMutation queue, optimistic UI, retry khi offline, ETag/If-Match concurrency control, server-wins conflict resolution khi gặp HTTP 412, FR-SYNC-4, NFR-REL); P3-14 xong (EventEditorDialog Figma S10, EventEditorViewModel, context menus cho sự kiện và ô trống với Shift+F10, kéo thả/điều chỉnh thời gian lưới 15 phút trong Tuần/Ngày, clipboard copy/paste, CRUD tích hợp PendingMutation và optimistic UI, FR-UI-4); P3-15 xong (Settings > Tài khoản Google Figma 04, chip Online/Offline, banner cảnh báo offline và hết hạn token, khối đồng bộ "Đồng bộ ngay", format relative sync time, custom OAuth overrides, local data wipe, onboarding wizard S14, FR-UI-9, FR-UI-10, FR-AUTH-1); P3-16 xong (SqliteSettingsStore persistent SQLite AppSetting store, seeding defaults, DI update, spec §6, spec §9, spec §13) | ADR-0013, ADR-0017, ADR-0018, ADR-0019, ADR-0026, ADR-0028, `src/LunarCal.Infrastructure/`, `tests/LunarCal.Infrastructure.Tests/`, `src/LunarCal.App/`, `tests/LunarCal.App.Tests/`, `docs/TASKS.md` |
| 4 | Đang thực hiện: P4-00..P4-11, P4-R1..P4-R3 đã hoàn tất [x]. Đang thực hiện P4-12..P4-13. | `phase/4`, `src/LunarCal.Core/`, `src/LunarCal.Infrastructure/`, `src/LunarCal.App/` |
| 5–8 | Chưa bắt đầu | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): `release.yml` (P8-02). Các mục gitleaks, CodeQL, SourceLink đều đã hoàn tất.

## Build, test, format (chạy ngày 2026-11-08, Windows 11, .NET SDK 10.0.403, Release)

Lần chạy mới nhất 2026-11-08 12:55 (Release, `task/P4-12`): build 0 warning, format pass, 753 test (177 Core, 337 Infra, 234 App, 5 UiTests): 753 pass, 0 fail, 0 skip. Coverage gate: Lunar 99.54%, Core 91.59% (ngưỡng 95% và 85%).

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx` | 753 test: 753 pass, 0 fail, 0 skip (2026-11-08 12:55) |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 99.54%, Core 91.59% (ngưỡng 95% và 85%; đo 2026-11-08 12:50) |
| UI smoke (`tests/LunarCal.App.UiTests`, 5 test) | Pass 5/5 (bao gồm Ai_confirm_dialog_smoke_test, Ai_command_bar_and_queue_drawer_smoke_test) |
| `docker build --target test .` (Linux) | Chưa chạy lại trong phiên này |

- **Số đo render MonthView (P2-07, NFR-PERF < 100 ms; đo lại ở P2-R6):** test `MonthViewRenderTests.NFR_PERF_Month_view_renders_in_under_100_ms_with_200_events` dựng `MonthView` thật trên luồng STA với `Tokens.xaml` + `Light.xaml`, 200 sự kiện/tháng, đo nạp dữ liệu + measure/arrange/layout khi đổi tháng, 12 lần mỗi lượt. Máy: Intel i7-12700H, 24 GB RAM, Release. Sau khi tái sử dụng ô và khe chip, median 20,3–22,7 ms, max 39,5–43,0 ms, lần đầu sau khởi động 21–26 ms.
- **Kiểm tra đổi theme khi đang chạy (P2-03):** smoke test `Settings_button_opens_appearance_page`: mở Cài đặt > Giao diện, chọn lần lượt Tối, Tương phản cao, Sáng, Theo hệ thống; app không thoát, radio được chọn đúng. Pass.

## Quyết định đã chốt mà spec chưa ghi

- ADR-0027 (2026-11-02): Claude Code review theo phase thay vì từng task. Task `[x]` = đã merge vào `phase/<N>`, chưa review. Cổng `P<N>-REVIEW` phải `[x]` (đã review, đã merge vào `main`) thì mới được sang phase sau.

Chi tiết nằm trong ADR (`docs/DECISIONS.md`):

- ADR-0003 AwesomeAssertions thay FluentAssertions (license). ADR-0004 xUnit v3 trên Microsoft.Testing.Platform. ADR-0005 `.slnx`.
- ADR-0006 lịch âm: phạm vi dương lịch 1900-01-01..2100-12-31, năm âm 1899..2100; ngoài phạm vi thì ném `ArgumentOutOfRangeException`; ngày âm không tồn tại thì `ToSolar` trả `null`; tháng nhuận dùng can chi của tháng thường; UTC+7 cho cả những năm trước 1968.
