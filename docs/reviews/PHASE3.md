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

## Lần 2: Claude Code sửa trực tiếp P3-R1..R4

Reviewer: Claude Code (Sonnet 5.5), 2026-11-04 23:02 +07:00 · Branch: phase/3 (đến commit 8690fc7) · Task: P3-R1..P3-R4 · Kết luận: **Chưa đóng cổng**. Mọi blocker của lần 1 đã sửa; còn hai bước DoD cần chủ dự án đăng nhập Google thật.

Cách kiểm: build Release 0 warning, `dotnet format --verify-no-changes` sạch, 490/490 test Release (UI smoke 3/3, 3 lần liên tiếp), coverage Lunar 100% và Core 97%; render dialog S10 offscreen bằng dữ liệu mẫu của Figma rồi đặt cạnh `35:774`.

## Blocker

- P3-R1 (FR-SYNC-1, FR-AUTH-1): DoD "chạy app thật: đăng nhập → lịch Google hiện trên lưới, không còn dữ liệu mẫu" chưa chạy được, cần tài khoản Google thật. Chủ dự án đăng nhập rồi xác nhận.
- P3-R3 (FR-UI-9): trang Cài đặt > Tài khoản ở trạng thái đã đăng nhập chưa được xem đặt cạnh Figma `4:2` (cần tài khoản thật).

## Đã sửa

- P3-R2/P3-14 (FR-UI-4): dựng lại S10 theo `35:774`: cột nhãn trái, phụ đề "lịch · sửa lần cuối …", nút đóng, segmented Không lặp/Dương lịch/Âm lịch, khối luật âm lịch (câu chính, chính sách, "Lần tới" 3 ngày do engine tính, "Tuỳ chỉnh…"), chip "Thông báo · …" + menu "+ Thêm nhắc nhở", múi giờ, "Ctrl+S"; bỏ các nút "+ 10m/+ 30m/…" ghi cứng; 4 key resx thiếu đã có · test `EventEditorS10Tests.FR_LUNAR_5_*`, `XamlLocalizationKeyTests.FR_I18N_3_Every_Loc_key_used_in_xaml_exists_in_vi_and_en_resx` · commit 8690fc7 · `src/LunarCal.App/Views/EventEditorDialog.xaml`
- P3-14 (FR-UI-4, tìm thấy khi dựng S10): `MainWindow` chưa bao giờ gọi `EventEditorViewModel.LoadCalendarsAsync`, nên hộp chọn lịch rỗng và mọi sự kiện được ghi vào calendar/account `"primary"` không có hàng `CalendarInfo` (lỗi khoá ngoại). Nay nạp lịch trước, mặc định lịch chính, và từ chối lưu khi không có lịch · test `FR_UI_4_Save_without_a_calendar_is_refused_instead_of_writing_to_a_made_up_primary`, `FR_UI_4_New_event_defaults_to_the_primary_calendar_of_its_account` · commit 8690fc7 · `src/LunarCal.App/Views/MainWindow.xaml.cs:53`
- P3-14 (FR-UI-4): Save bỏ mất nhắc nhở (sửa sự kiện còn đặt lại thành 10 phút) và mọi lặp dương lịch, dù model, gateway và SQLite đã chứa sẵn. Nay ghi cả hai; sự kiện dùng nhắc nhở mặc định của lịch giữ nguyên mặc định, RRULE mà editor không biểu diễn được (BYDAY, COUNT…) được giữ nếu không đổi tần suất · test `FR_UI_4_Save_writes_the_chosen_reminders_and_solar_recurrence`, `..._Removing_every_reminder_saves_an_empty_list_...`, `..._Editing_an_event_on_calendar_default_reminders_keeps_them_default`, `..._Editing_a_recurring_master_keeps_a_rule_the_editor_cannot_express`, `..._Switching_a_recurring_master_to_no_repeat_removes_its_rule` · commit 8690fc7 · `src/LunarCal.App/ViewModels/EventEditorViewModel.cs`
- P3-14 (FR-LUNAR-5): sửa sự kiện luật âm lịch sinh `lunarRuleId` mới mỗi lần lưu → giữ id cũ · commit 8690fc7
- P3-14: xoá sự kiện dùng lịch trong hộp chọn thay vì lịch gốc của sự kiện → dùng `CalendarId` gốc · commit 8690fc7
- P2-12/spec §9: UI smoke `Settings_button_opens_appearance_page` fail lặp lại với `NoClickablePointException` (bấm chuột vào nav ngay lúc cửa sổ vừa mở, chưa có điểm bấm) → chọn qua UIA SelectionItem như các radio theme; pass 3/3 ba lần liên tiếp · commit 37dcf37 · `tests/LunarCal.App.UiTests/SmokeTests.cs:215`
- Hai test cũ (`FR_UI_4_Validation_requires_title_and_valid_time_interval`, `FR_UI_4_DuplicateAsync_creates_copy_with_shifted_time`) chạy khi chưa nạp lịch, tức dựa vào chính fallback `"primary"`; đổi sang nạp lịch trước, không nới quy tắc.

## Nên sửa sau

- P3-09: khoá chính `Event.id` là id Google, chỉ duy nhất trong từng lịch; sự kiện xuất hiện ở hai lịch của cùng tài khoản bị upsert chuyển qua lại. Cần khoá `(calendar_id, id)` kèm migration dựng lại bảng và sửa mọi chỗ tra theo id (`GetEventByIdAsync`, mutation). Chưa làm vì đổi schema, nên chờ chủ dự án. `src/LunarCal.Infrastructure/Storage/EventRows.cs:43`
- S10 (thiết kế): Figma không có chọn tần suất cho "Dương lịch" (code thêm một dropdown Hàng ngày/tuần/tháng/năm), không có ngày kết thúc cho sự kiện có giờ (dialog chỉ hiện ngày kết thúc khi sự kiện cả ngày hoặc đã nhiều ngày, nên chưa tạo được sự kiện qua đêm), và dòng múi giờ là chữ tĩnh (Figma vẽ "▾"). Chủ dự án cập nhật Figma hoặc chốt hành vi.
- P3-14/FR-LUNAR-4: luật âm lịch chỉ lưu thành thuộc tính mở rộng; vật chất hoá và "Đồng bộ lên Google" thuộc P5-02/P5-03. Hiện công tắc chỉ lưu cờ.
- Test `NFR_PERF_Month_view_renders_in_under_100_ms_with_200_events` đỏ 1 lần khi chạy cả solution song song (median chạy riêng ~28 ms, ngưỡng 100 ms); lần chạy lại xanh. Nên cô lập khỏi các project test chạy song song.
- P3-R3: bỏ nút "Tìm hiểu"/"Xem hướng dẫn" vì chưa có URL trợ giúp đã xác minh; bước 3 Gemini của S14 để Phase 4.

## OK

- P3-R4: xác nhận trước khi đăng xuất và xoá dữ liệu; secret OAuth tuỳ chỉnh chỉ ở DPAPI, Client ID tuỳ chỉnh dùng thật trong URL authorize (đã có test).
- P3-R1 (phần kiểm được bằng test): `SqliteEventStore` đọc cache SQLCipher, host `StartAsync`/`StopAsync`, S14 tự mở lần đầu, sidebar "chưa đồng bộ", `SyncCompleted` về Dispatcher. `SyncEngine` đã bọc `try/catch` cả nhánh không có syncToken lẫn nhánh 410 (mục "SyncEngine ngoài try" của lần 1 đã xử lý).
- Kiến trúc và analyzer: Core không thêm tham chiếu; không thêm `NoWarn`, `#pragma` hay `SuppressMessage` (đã kiểm bằng diff từ b99364d); chevron/mũi tên là path token (quy tắc `FR_UI_11` không dùng ký tự biểu tượng vẫn xanh).
