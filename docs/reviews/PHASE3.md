# Review Phase 3: Google OAuth, SyncEngine, SQLCipher cache, CRUD

Reviewer: Claude Code (Opus 5.5), 2026-11-04 00:34 +07:00 · Branch: phase/3 · Task: P3-00a..P3-16 · Kết luận: Trả lại

Cách kiểm: chạy lại build Release, format, test và cổng coverage trên `phase/3`; chạy `LunarCal.exe` thật; render offscreen S10, Figma 04, S14, S20 rồi đặt cạnh frame Figma (`35:774`, `4:2`, `39:280`, `34:191`). 439 test xanh không chứng minh được DoD: app thật chưa nối OAuth → sync → hiển thị, và S10 crash khi mở.

## Blocker

- P3-09/P3-16 (DoD "FR-SYNC đạt"): UI vẫn đọc `InMemoryEventStore` (dữ liệu mẫu). Sự kiện sync về SQLCipher không bao giờ hiển thị; tạo/sửa trên lịch mẫu ghi vào bảng `Event` sẽ lỗi FK vì `CalendarInfo` không có lịch đó. `src/LunarCal.Infrastructure/DependencyInjection.cs:27` → task P3-R1.
- P3-12 (FR-SYNC-3): host không bao giờ `StartAsync`, nên `SyncScheduler` (IHostedService) không chạy: không có sync lúc khởi động, không polling 2/10 phút. `src/LunarCal.App/App.xaml.cs:78` → P3-R1.
- P3-15 (FR-AUTH-1, S14): cửa sổ lần chạy đầu chỉ mở khi có tham số `--first-run`, không tự hiện ở lần chạy đầu. `src/LunarCal.App/App.xaml.cs:90` → P3-R1.
- P3-15 (FR-UI-10): khối đồng bộ ở sidebar ghi "Đã đồng bộ với Google Calendar · Vừa xong" khi chưa đăng nhập và chưa sync lần nào. `src/LunarCal.App/ViewModels/MainWindowViewModel.cs:180` → P3-R1.
- P3-14 (FR-UI-4, Figma S10 `35:774`): dialog lệch hẳn thiết kế: không có cột nhãn trái, phụ đề, nút đóng; "Lặp lại" là ComboBox thay vì segmented 3 lựa chọn; thiếu khối luật âm lịch (lần tới, tuỳ chỉnh); chip nhắc nhở "Thông báo · …" và "+ Thêm nhắc nhở" bị thay bằng nút ghi cứng "+ 10m/+ 30m/+ 1h/+ 1d"; thiếu "(GMT+07:00) Hồ Chí Minh" và gợi ý Ctrl+S. `src/LunarCal.App/Views/EventEditorDialog.xaml:365` → P3-R2.
- P3-14 (FR-I18N): 4 key UI không có trong resx nào: `common.title`, `reminder.label`, `lunar_rule.annual_on`, `lunar_rule.is_leap` (hiện "!common.title!"). `src/LunarCal.App/Views/EventEditorDialog.xaml` → P3-R2.
- P3-15 (Figma 04 `4:2`, S14 `39:280`, S20 `34:191`): ba màn hình lệch thiết kế. Trang Tài khoản là một thẻ riêng thay vì hàng "Đồng bộ lần cuối… / Đồng bộ ngay" và nút "Ngắt kết nối". S14 thiếu cột stepper 1/2/3, mô tả từng ngôn ngữ, công tắc Âm lịch. S20 thiếu công tắc "Dùng OAuth client riêng", ô secret che có "Hiện", ghi chú Testing, "Dùng client mặc định", khối Dung lượng và Mức ghi nhật ký. Chuỗi UI còn lộ mã yêu cầu, ví dụ "OAuth Client ID riêng (FR-AUTH-6)". `src/LunarCal.App/Localization/Strings.vi-VN.resx:886` → P3-R3.
- P3-07/P3-04 (FR-AUTH-4, SEC-DATA-3): đăng xuất truyền cứng `confirmed: true`, còn "Xoá toàn bộ dữ liệu cục bộ" chạy ngay. Cả hai không có bước xác nhận như spec yêu cầu. `src/LunarCal.App/ViewModels/GoogleAccountViewModel.cs:264`, `src/LunarCal.App/ViewModels/AdvancedSettingsViewModel.cs:105` → P3-R4.
- P3-15 (FR-AUTH-6, security.md): Client ID/secret tuỳ chỉnh được lưu nhưng không nơi nào đọc lại (`GoogleOAuthOptions` chỉ lấy từ cấu hình build). Client secret nằm trong `AppSetting` thay vì `ISecretStore` (Figma S20 ghi "Lưu mã hoá trên máy (DPAPI)"). `src/LunarCal.App/ViewModels/AdvancedSettingsViewModel.cs:77` → P3-R4.

## Đã sửa

- P3-14: mở S10 là crash (`XamlParseException`) vì XAML dùng `DateOnlyToDateTimeConverter`, `TimeOnlyToStringConverter`, `StringToVis` không tồn tại, và `CommandParameter="10"` (chuỗi) cho `RelayCommand<int>` → thêm 2 converter, khai báo resource, truyền `Int32`; test `FR_UI_4_Event_editor_dialog_opens_and_binds_start_date_and_time` · commit b4f5b26, 77d005b (cô lập test khỏi các test STA chạy song song) · `src/LunarCal.App/Views/EventEditorDialog.xaml:26`
- P3-15/P3-12: `SqliteAccountStore` và `SyncScheduler.GetAccountIdsAsync` chạy lệnh trên kết nối chưa mở, nên đăng nhập OAuth thành công rồi vẫn báo lỗi khi lưu account, `GetPrimaryAccountAsync` luôn trả null, và scheduler không bao giờ thấy account để sync → thêm `OpenAsync`; test `FR_AUTH_1_Saved_account_round_trips_through_sqlite_and_FR_AUTH_4_delete_removes_it`, `FR_SYNC_3_Scheduler_syncs_accounts_stored_in_sqlite` · commit f6b27b9 · `src/LunarCal.Infrastructure/Storage/SqliteAccountStore.cs:38`

## Nên sửa sau

- P3-09: bảng `Event` có khoá chính là `id` (id sự kiện Google), mà id này chỉ duy nhất trong từng calendar. Một cuộc họp xuất hiện ở hai lịch sẽ bị upsert chuyển qua lại giữa hai lịch. Nên dùng khoá `(calendar_id, id)` kèm migration V2. `src/LunarCal.Infrastructure/Google/SyncEngine.cs:477`
- P3-10: nhánh "calendar chưa có syncToken" và nhánh 410 gọi `FetchFullCalendarEventsAsync` ngoài try, nên lỗi mạng sẽ văng exception thay vì trả `SyncResult.Failed`. `src/LunarCal.Infrastructure/Google/SyncEngine.cs:199`
- P3-12/P3-15: `SyncCompleted` được raise trên thread pool, còn `MainWindowViewModel` gọi `ReloadActiveViewAsync` (đổi collection bind UI) ngay trong handler. Cần marshal về Dispatcher. `src/LunarCal.App/ViewModels/MainWindowViewModel.cs:215`
- P3-14: chưa chọn lịch thì `EventEditorViewModel` dùng account/calendar `"primary"` làm mặc định, nên sẽ ghi vào bảng `Event` với calendar không tồn tại. `src/LunarCal.App/ViewModels/EventEditorViewModel.cs:511`
- Test: `LunarPerformanceTests.FR_LUNAR_2_A_month_grid_is_computed_in_under_5_ms` (Phase 1) fail 1/4 lần khi chạy cả solution trên máy đang tải nặng. Đây là test đo thời gian, không phải lỗi logic.

## OK

- Build Release 0 warning; `dotnet format --verify-no-changes` pass; 442 test (sau 3 test mới) pass; coverage Lunar 100%, Core 97%.
- SEC-OAUTH-1 (PKCE S256, `state`, loopback 127.0.0.1, timeout), SEC-NET-1/2 (allow-list, HTTPS, không retry ghi thiếu requestId/ETag), FR-SYNC-5 (backoff + throttle bằng `IClock`), FR-SYNC-2 (410 → full resync), FR-SYNC-4 (412 server thắng) có test cụ thể với fake handler; review nhẹ phần này ở mức đọc test.
- SQLCipher: khoá 256-bit bọc DPAPI, DB có sẵn mà mất khoá thì từ chối mở; migration V1 đủ 9 bảng spec §6 và index `(calendar_id, start_utc)`.
- Kiến trúc: Core không thêm tham chiếu ngoài BCL; không thấy `NoWarn`, `#pragma` mới hay bypass chứng chỉ.
