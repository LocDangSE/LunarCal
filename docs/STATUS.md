# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-08 08:45 +07:00, Antigravity. Hoàn thành P4-08 (Hiện thực lưu trữ `AiCommand` vào SQLite với `display_no` tự tăng từ 1000 `#1000`, `typed_at` làm mốc thời gian tương đối, `input_lang`, `timezone`, `requestId` idempotent dạng `lunarcal-ai-{id}`; 6 trạng thái `pending`, `processing`, `needs_confirmation`, `error`, `completed`, `cancelled` khớp `AccountDataPurger.TerminalAiCommandStatuses`; worker hàng chờ `AiCommandQueueWorker` hosted service với crash recovery `processing -> pending`, retry tối đa 5 lần theo backoff lũy thừa có jitter `AiQueueRetryPolicy`, tự động dọn lệnh đã hoàn tất/huỷ sau 30 ngày theo FR-QUEUE-1, 4, 5, 8, 9; 13 unit test mới). Toàn bộ 679/679 test pass (155 Core, 316 Infra, 205 App, 3 UiTests), format sạch, coverage Lunar 99.54% và Core 91.35%. Đã merge `task/P4-08` vào `phase/4` (commit 6a42ddd). Task tiếp theo: P4-09.

**Phiên trước:** 2026-11-08 08:30 +07:00, Antigravity. Hoàn thành P4-07 (`ChatOrchestrator` state machine, xác thực và thi hành, cam kết không ghi khi chưa xác nhận, FR-CHAT-1, FR-CHAT-2, SEC-PI-2). Merge commit 749ddcd trên phase/4.

**Trước đó:** 2026-11-08 08:12 +07:00, Antigravity. Hoàn thành P4-06 (Schema 7 tool, ToolCallValidator, ToolDefinitions limits). Merge commit e57b755 trên phase/4.

## Phase và task

- **Phase hiện tại:** Phase 4, nhánh `phase/4`. `P4-00`, `P4-01`, `P4-02`, `P4-03`, `P4-04`, `P4-05`, `P4-06`, `P4-07`, `P4-08`, `P4-R1`, `P4-R2`, `P4-R3` `[x]`. Còn P4-09..P4-13.
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ branch. P4-R2 xong và đã merge vào `phase/4`. Review Phase 4 khi cổng `P4-REVIEW` ở `[r]`.
  - **Antigravity:** Đã hoàn thành P4-01..P4-08, P4-R1..P4-R3. Toàn bộ 679/679 test pass, format sạch, coverage gate đạt (Lunar 99.54%, Core 91.35%). Đã merge vào `phase/4`. Sẵn sàng nhận task tiếp theo: P4-09 (FR-UI-5, FR-QUEUE-2, 3, 6, 7, FR-I18N-2: `Controls/AiCommandBar`, `Views/AiQueueDrawer` Figma 01, 03: Thanh lệnh, chip VI · EN, banner offline, ngăn kéo hàng chờ AI với hành động theo trạng thái, kiểm thử UI đối chiếu Figma).
    - Đọc trước khi làm Phase 4 (spec đã cập nhật 2026-11-07): ADR-0016 (Gemini, bảo vệ key), ADR-0029 (một tài khoản), ADR-0030 (vị trí prompt), spec §4.5 (FR-BYOK-1..7), §5.1 (SEC-KEY-6..11), §7 (Tích hợp Gemini), FR-CONFIRM-8, FR-QUEUE-2 (mã trạng thái `AiCommand`: `pending`, `processing`, `needs_confirmation`, `error`, `completed`, `cancelled`) và §13. Thiết kế đã duyệt, node id ở `.agents/rules/ui.md`: Figma 01 (`1:2`), Figma 03 (`1:100`), S14 1.3, S16 4.4, S18 7.1, S20 5.3.
- **Task tiếp theo đề xuất:**
  1. Antigravity: Bắt đầu P4-09 (FR-UI-5, FR-QUEUE-2, 3, 6, 7, FR-I18N-2: `Controls/AiCommandBar`, `Views/AiQueueDrawer` Figma 01, 03: thanh lệnh, chip VI · EN, banner offline, ngăn kéo hàng chờ AI, chạy app và kiểm thử đối chiếu Figma 100%).
  2. Chủ dự án: push `phase/4`, `main` và `task/P4-R2` rồi xem CI trên GitHub (tab Actions).
  3. Chủ dự án: kiểm tra thủ công đăng nhập tài khoản Google khác trên app thật: hộp thoại hiện, huỷ giữ nguyên dữ liệu, đồng ý thì lịch mới được tải về.

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | Hoàn thành 100% (P0-00, P0-01, P0-02, P0-03, P0-04); CodeQL nâng lên 4.39.1; CI xanh trên GitHub | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, `codeql.yml`, ADR-0001..0005, ADR-0023, `.gitleaks.toml`, SourceLink trong PDB |
| 1 | Xong: FR-LUNAR-1..3, coverage Lunar 100%, CI xanh | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2 | Hoàn thành 100%: P2-00 (Figma S06–S09, S13 và design token), P2-01 (resx, i18n), P2-02 (pseudo-locale), P2-03 (theme & tokens, ADR-0011), P2-04 (settings store), P2-05 (event model & sample store), P2-06 (MainWindow shell, TopBar, Sidebar, MiniCalendar), P2-06b (cân chỉnh tokens theo Figma), P2-07 (MonthView 6×7 grid, lunar formatting, render < 100 ms), P2-08 (WeekView, DayView, timed event grid, now line, lane clustering), P2-09 (AgendaView, date grouping, calendar filters), P2-10 (DayDetailPanel, Can Chi, solar terms, glosses), P2-11 (AppearancePage, week start, 12h/24h, timezone), P2-12 (FlaUI UI smoke tests out-of-process), P2-13 (kết thúc Phase 2, cập nhật tài liệu, kiểm tra gates) | `tests/LunarCal.App.UiTests/SmokeTests.cs`, `src/LunarCal.App/Views/Settings/AppearancePage.xaml`, `DayDetailPanel.xaml`, `AgendaView.xaml`, `WeekView.xaml`, `DayView.xaml`, `MonthView.xaml`, `MainWindow.xaml`, `tests/`, `docs/TRACEABILITY.md`, `CHANGELOG.md` |
| 3 | Hoàn thành 100% (P3-00a..P3-16): P3-00a xong (Client ID trong user-secrets, consent Testing); P3-00b xong (Figma S10, S14, S18, S20); P3-01 xong (Serilog file sink xoay vòng 14 ngày, LogRedactor che bí mật, SEC-LOG-1, SEC-LOG-2); P3-02 xong (ISecretStore, DpapiSecretStore, SEC-KEY-1, SEC-OAUTH-3, FR-AUTH-3, ADR-0018); P3-03 xong (SQLCipher encrypted storage, migration runner V1, 9 bảng spec §6, index, SEC-DATA-1, ADR-0013); P3-04 xong (ACL thư mục SEC-DATA-2, LocalDataWipeService SEC-DATA-3); P3-05 xong (Google OAuth desktop PKCE S256, loopback 127.0.0.1, state, timeout 5 phút, scope tối thiểu, ADR-0017 Accepted, FR-AUTH-1, FR-AUTH-2, FR-AUTH-6, SEC-OAUTH-1, SEC-OAUTH-2); P3-06 xong (Tự động refresh token, invalid_grant đăng nhập lại, hướng dẫn consent Testing hết hạn sau 7 ngày, FR-AUTH-3, FR-AUTH-6); P3-07 xong (Đăng xuất Google account: revoke endpoint, xoá refresh token DPAPI, purge toàn bộ cache Account/CalendarInfo/Event/ReminderState/LunarRule khỏi SQLite có xác nhận, FR-AUTH-4); P3-08 xong (HttpClient resilience pipeline, allow-list host, HTTPS only, timeout, circuit breaker, retry idempotency filter, SEC-NET-1, SEC-NET-2); P3-09 xong (Google Calendar API v3 Gateway và SyncEngine: đồng bộ lần đầu calendar list và events [-12m, +24m], singleEvents=true, SQLite persistence, extended_json, FR-SYNC-1, FR-SYNC-6, ADR-0028); P3-10 xong (Incremental sync bằng syncToken, xử lý sự kiện bị huỷ, upsert, tự động kích hoạt full resync khi 410 Gone, FR-SYNC-2); P3-11 xong (Quota management và throttling <= 5 req/s via IClock, exponential backoff và jitter cho 429 và 403 rateLimitExceeded, FR-SYNC-5); P3-12 xong (Hosted background sync scheduler, polling thích ứng 2m active / 10m tray, trigger tức thời focus / post-mutation / thủ công, coalescing, FR-SYNC-3); P3-13 xong (PendingMutation queue, optimistic UI, retry khi offline, ETag/If-Match concurrency control, server-wins conflict resolution khi gặp HTTP 412, FR-SYNC-4, NFR-REL); P3-14 xong (EventEditorDialog Figma S10, EventEditorViewModel, context menus cho sự kiện và ô trống với Shift+F10, kéo thả/điều chỉnh thời gian lưới 15 phút trong Tuần/Ngày, clipboard copy/paste, CRUD tích hợp PendingMutation và optimistic UI, FR-UI-4); P3-15 xong (Settings > Tài khoản Google Figma 04, chip Online/Offline, banner cảnh báo offline và hết hạn token, khối đồng bộ "Đồng bộ ngay", format relative sync time, custom OAuth overrides, local data wipe, onboarding wizard S14, FR-UI-9, FR-UI-10, FR-AUTH-1); P3-16 xong (SqliteSettingsStore persistent SQLite AppSetting store, seeding defaults, DI update, spec §6, spec §9, spec §13) | ADR-0013, ADR-0017, ADR-0018, ADR-0019, ADR-0026, ADR-0028, `src/LunarCal.Infrastructure/`, `tests/LunarCal.Infrastructure.Tests/`, `src/LunarCal.App/`, `tests/LunarCal.App.Tests/`, `docs/TASKS.md` |
| 4 | Đang thực hiện: P4-00..P4-08, P4-R1..P4-R3 đã hoàn tất [x]. Đang thực hiện P4-09..P4-13. | `phase/4`, `src/LunarCal.Core/`, `src/LunarCal.Infrastructure/`, `src/LunarCal.App/` |
| 5–8 | Chưa bắt đầu | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): `release.yml` (P8-02). Các mục gitleaks, CodeQL, SourceLink đều đã hoàn tất.

## Build, test, format (chạy ngày 2026-11-08, Windows 11, .NET SDK 10.0.403, Release)

Lần chạy mới nhất 2026-11-08 08:42 (Release, `phase/4`): build 0 warning, format pass, 679 test (155 Core, 316 Infra, 205 App, 3 UiTests): 679 pass, 0 fail, 0 skip. Coverage gate: Lunar 99.54%, Core 91.35% (ngưỡng 95% và 85%).

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx` | 679 test: 679 pass, 0 fail, 0 skip (2026-11-08 08:42) |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 99.54%, Core 91.35% (ngưỡng 95% và 85%; đo 2026-11-08 08:42) |
| UI smoke (`tests/LunarCal.App.UiTests`, 3 test) | Pass 3/3 trong lần chạy cả solution |
| `docker build --target test .` (Linux) | Chưa chạy lại trong phiên này |

- **Số đo render MonthView (P2-07, NFR-PERF < 100 ms; đo lại ở P2-R6):** test `MonthViewRenderTests.NFR_PERF_Month_view_renders_in_under_100_ms_with_200_events` dựng `MonthView` thật trên luồng STA với `Tokens.xaml` + `Light.xaml`, 200 sự kiện/tháng, đo nạp dữ liệu + measure/arrange/layout khi đổi tháng, 12 lần mỗi lượt. Máy: Intel i7-12700H, 24 GB RAM, Release. Sau khi tái sử dụng ô và khe chip, median 20,3–22,7 ms, max 39,5–43,0 ms, lần đầu sau khởi động 21–26 ms.
- **Kiểm tra đổi theme khi đang chạy (P2-03):** smoke test `Settings_button_opens_appearance_page`: mở Cài đặt > Giao diện, chọn lần lượt Tối, Tương phản cao, Sáng, Theo hệ thống; app không thoát, radio được chọn đúng. Pass.

## Quyết định đã chốt mà spec chưa ghi

- ADR-0027 (2026-11-02): Claude Code review theo phase thay vì từng task. Task `[x]` = đã merge vào `phase/<N>`, chưa review. Cổng `P<N>-REVIEW` phải `[x]` (đã review, đã merge vào `main`) thì mới được sang phase sau.

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
- P2-R1: múi giờ hiển thị lấy từ setting `timezone` (mặc định múi hệ thống); sự kiện cả ngày giữ ngày theo múi của chính sự kiện (`CalendarEvent.TimeZone`), không dời theo múi người xem.
- P2-R4: ký hiệu tháng nhuận ở ô lịch tiếng Anh là "L" (ví dụ `1/2L`), tiếng Việt giữ "n". "Đủ/Thiếu" ở tiêu đề âm lịch tiếng Anh là "30 days/29 days". Spec và Figma chưa quy định; đổi được trong resx (`lunar.leap_marker`, `lunar.month_full`, `lunar.month_short`).
- P2-R2: Cài đặt > Giao diện (S13) tạm mở bằng cửa sổ con từ nút ⚙ trên thanh trên cùng, chờ màn hình Cài đặt đầy đủ ở phase sau.
- ADR-0029 (2026-11-07): chỉ một tài khoản Google; đăng nhập tài khoản khác thì xoá dữ liệu cũ và nạp dữ liệu tài khoản mới; đăng nhập lại cùng tài khoản không xoá gì. Bỏ FR-AUTH-5. Spec đã sửa; đã triển khai ở P4-R2 (2026-11-07).
- ADR-0016 (2026-11-07): chủ dự án xác nhận Gemini key chỉ ở máy người dùng và dùng gói miễn phí. Model mặc định `gemini-3.8-flash`, lọc model hỗ trợ `generateContent` loại `tts`, `live`, `image`, `embedding`, `omni`, `latest`, `gemini-2.5-*`, probe `test_ping` xác nhận function calling.
- ADR-0026: consent screen để ở Testing (refresh token hết hạn sau 7 ngày, xử lý ở P3-06).
- ADR-0030 (2026-11-08): Vị trí lưu trữ và cơ chế nạp System Prompt có phiên bản: đặt tại `src/LunarCal.Core/Prompts/system.{version}.md` dưới dạng EmbeddedResource, nạp qua `ISystemPromptProvider`, context injection qua `PromptContext` đảm bảo zero event data rò rỉ vào prompt.

## Câu hỏi mở

### Q-001: Câu hỏi bắt buộc cho chủ dự án (spec §14)

- **Mục spec:** §14
- **Loại:** Thiếu đặc tả (thuộc danh sách được phép hỏi)
- **Mô tả:** Chưa có câu trả lời cho: (1) biểu tượng, nhà phát hành (tên app đã chốt là LunarCal ngày 2026-11-01, ADR-0023); (2) OAuth Client ID và trạng thái consent screen; (3) chứng chỉ ký mã và kênh cập nhật; (4) mặc định có tự khởi động cùng Windows không; (5) nhà cung cấp, vùng và ngân sách cloud; (6) bên kiểm soát dữ liệu, liên hệ quyền riêng tư, độ tuổi tối thiểu; (7) xác nhận hai quyết định ở §15.3 (key Gemini chỉ ở máy người dùng; lịch âm mặc định lưu cục bộ); (8) bản tiếng Anh cho các chuỗi giao diện còn lại.
- **Cập nhật 2026-11-01 18:40 (Claude Code):** Câu (2) đã trả lời: chủ dự án đã tạo Client ID và lưu vào user-secrets; consent screen để ở Testing (ADR-0026).
- **Cập nhật 2026-11-07 (Claude Code):** Câu (7), phần Gemini key: chủ dự án xác nhận key chỉ lấy và lưu trên máy người dùng. Phần "lịch âm mặc định lưu cục bộ" chưa được nhắc tới, tạm giữ theo spec.
- **Ảnh hưởng:** P6b-00, P7-00, P8-00; các task resx từ P2-01.
- **Đề xuất / tạm quyết:** Code hiện theo spec (key ở máy, lịch âm cục bộ). Không chặn Phase 2 trừ câu (8), với câu này tạm dùng bản dịch nháp.
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-002: Thiết kế Figma cho Phase 2 (Đã đóng)
- **Trạng thái:** Đã đóng (2026-11-01)

### Q-003: Bốn năm nhuận chưa có nguồn đối chiếu thứ hai
- **Trạng thái:** Tạm quyết (ADR-0009)

### Q-004: Quyền phân phối lại dữ liệu HKO trong repo công khai
- **Trạng thái:** Mở
