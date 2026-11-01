# Review Phase 2 (P2-00..P2-13)

- Người review: Claude Code (claude-opus-5-5), 2026-11-01. Chỉ đọc code, không sửa.
- Phạm vi: `git diff 159781c..HEAD` (115 file, +13 475 dòng). Review sâu P2-01, P2-07, P2-10, P2-11, P2-12; kiểm tra token cho P2-03/P2-06; kiểm tra `docs/STATUS.md`.
- Không chạy lại build/test. CI không kiểm tra được vì máy không có `gh`.

## Kiểm tra nhanh (bước 1)

| Mục | Kết quả |
| --- | --- |
| `Skip`, `[Ignore]` trong tests/ | Không có |
| `Assert.True(true)` | Không có. Có 9 chỗ `NotBeNull()`, phần lớn có assert tiếp theo; 2 chỗ yếu ghi ở P2-01 và P2-12 |
| `TODO`, `NotImplementedException`, `#pragma warning disable`, `NoWarn` trong src/ (*.cs, *.xaml, *.csproj, *.props) | Không có |
| Tên test trong cột "Xong khi" | Có đủ 8/8. `FR_UI_7_Theme_follows_system_setting` tồn tại dưới dạng 2 test có hậu tố `_when_system_is_dark/_light` |
| Thứ tự làm | P2-03 (11:46) và P2-06 (12:06) xong trước P2-00 (12:48). P2-06b (13:02) đã cân chỉnh token sau đó, nhưng view vẫn còn giá trị hard-code (xem P2-03/P2-06) |

## Vấn đề chung (ảnh hưởng nhiều task)

- **Múi giờ +7 bị hard-code** ở 6 chỗ: `DayDetailViewModel.cs:229`, `AgendaViewModel.cs:129`, `:198`, `DayColumnViewModel.cs:153`, `DayViewModel.cs:130`, `WeekViewModel.cs:163`. `MonthViewModel.cs:210-216` dùng nửa đêm UTC kết hợp `LocalDateTime`. Không chỗ nào đọc setting timezone.
- **Chuỗi UI hard-code trong VM**: 21 chỗ có "Tháng", "Nhuận", "Leap", "Month " trong `ViewModels/`. Trong XAML: `MainWindow.xaml:129`, `DayView.xaml:98`, `MiniCalendar.xaml:136`, `Sidebar.xaml:175`. Vi phạm `.agents/rules/i18n.md`.
- **Màu fallback hard-code trong VM**: `#2F6BFF` có ở 7 chỗ, `#4285F4` ở `CalendarItemViewModel.cs:17` và `MainWindowViewModel.cs:269`. Hai giá trị không thống nhất với nhau.

---

## P2-00 · Figma S06–S09, S13, token

- **Blocker:** không có.
- **Nên sửa:** task bị xong sau P2-03/P2-06, trái với ràng buộc "Chặn các task UI P2-06..P2-11" ghi trong `TASKS.md:24`. Lần sau nên tuân thủ thứ tự hoặc ghi ADR giải thích.
- **OK:** bảng token đã có trong `.agents/rules/ui.md` (từ dòng 44).

## P2-01 · Resx, đổi ngôn ngữ, mặc định theo OS

- **Blocker:** không có.
- **Nên sửa:**
  - `LocalizationTests.cs:43` assert `BeOneOf("vi-VN","en-US")` nên luôn pass. Spec FR-I18N-1 yêu cầu vi-VN khi không xác định được, nên phải assert `Be("vi-VN")`.
  - `LocalizationService.cs:27` chỉ dùng culture của OS và bỏ qua setting `ui_language` đã lưu. Tạm chấp nhận vì store ở Phase 2 nằm trong bộ nhớ, nhưng phải xử lý khi làm P3-16.
- **OK:** test `FR_I18N_3_Every_key_has_non_empty_vi_and_en` đọc trực tiếp file resx và kiểm tra cả hai chiều cùng giá trị rỗng. `FR_I18N_7_...` (`LocalizationTests.cs:240`) phủ đủ 21 key của bảng §4.11. Bảng Toast có test riêng. Đã có ADR-0024 về quy ước key.

## P2-02 · Pseudo-locale

- **Blocker:** không có.
- **Nên sửa:** không có (không review sâu).
- **OK:** có `FR_I18N_3_Pseudo_locale_transforms_every_key`, kèm test placeholder `{0}` và test bật/tắt bằng cờ cấu hình.

## P2-03 · Theme, ADR-0011

- **Blocker:**
  - "Xong khi" yêu cầu "đổi theme khi đang chạy không lỗi (kiểm tra thủ công, ghi vào STATUS)", nhưng `docs/STATUS.md` không ghi kết quả kiểm tra thủ công nào. Chỉ có dòng ADR ở `STATUS.md:55`.
- **Nên sửa:**
  - Còn kích thước hard-code, trái với `ui.md:44`: `FontSize` ở `AgendaView.xaml:154`, `DayDetailPanel.xaml:64,110,265,322`, `MainWindow.xaml:123`, `Sidebar.xaml:176`. `CornerRadius` ở `MainWindow.xaml:110,137`, `AgendaView.xaml:196,263`, `DayDetailPanel.xaml:354`.
  - `Sidebar.xaml:178` có `Foreground="#000000"` nên sẽ không đổi theo theme tối hoặc HC.
- **OK:** test service theme phủ đủ system dark/light, ghi đè, HC ưu tiên, event `ThemeChanged`. `ThemeDictionaryTests` kiểm tra 3 theme có cùng bộ key. View chỉ dùng `StaticResource` cho converter, không dùng cho brush.

## P2-04 · Settings store

- **Blocker:** không có.
- **Nên sửa:** không có.
- **OK:** `SettingsDefaultsTests.cs:19` phủ week_start T2 với vi và CN với en; lunar mặc định bật.

## P2-05 · Event model, fake store

- **Blocker:** không có.
- **Nên sửa:** không có (không review sâu).
- **OK:** có test truy vấn theo khoảng ngày (trùng biên, nằm ngoài khoảng, khoảng ngược) và test `AccountId` cascade.

## P2-06 · Shell (TopBar, Sidebar, MiniCalendar)

- **Blocker:**
  - `MainWindowViewModel.cs:289` đặt đầu tuần theo culture mỗi lần đổi ngôn ngữ (`vi→Mon`, `en→Sun`), ghi đè setting `week_start` của người dùng. Lỗi tương tự ở `:317` khi tính tiêu đề Tuần.
- **Nên sửa:**
  - `MainWindow.xaml:129` có placeholder thanh AI viết cứng tiếng Việt. `Sidebar.xaml:175` có `'{0} lệnh đang chờ'` viết cứng. `MiniCalendar.xaml:136` có `AutomationProperties.Name` viết cứng "Ngày …".
  - `MainWindowViewModel.cs:276` dùng `catch` rỗng nuốt mọi lỗi khi nạp calendar và không ghi log.
- **OK:** có test chuyển view, nút Hôm nay, VI | EN đổi ngay (`MainWindowViewModelTests.cs:36-158`).

## P2-07 · MonthView 6×7 (rủi ro cao)

- **Blocker:**
  - NFR-PERF chưa thật sự được đo. `MonthViewModel.cs:166-230` chỉ bấm giờ phần dựng dữ liệu VM, và dừng đồng hồ trước `Days.Clear/Add` (dòng 232-236), tức chưa tính layout/render WPF. Test `MonthViewModelTests.cs:115` dùng store rỗng.
  - `STATUS.md:26` chỉ ghi "render < 100 ms", không có con số đo cụ thể, máy đo hay số mẫu. Như vậy chưa đạt mục "ghi kết quả vào STATUS".
  - `MonthViewModel.cs:210-211` truy vấn từ nửa đêm UTC. Với UTC+7, sự kiện từ 00:00 đến 07:00 giờ địa phương của ngày đầu lưới bị bỏ sót, còn cuối lưới thì lấy thừa.
- **Nên sửa:**
  - `MonthViewModel.cs:170` viết cứng `"Tháng {m}, {y}"`. Dòng `:250-256` viết cứng "Th 2".."CN". Cả hai phải lấy từ resx.
  - `MonthDayViewModel.cs:120` viết cứng hậu tố nhuận `"n"`, ở chế độ en vẫn hiện `1/2n`. Cần ký hiệu theo resx hoặc theo Figma S09.
  - `MonthViewModel.cs:67`, `:93`, `:269` gọi `PopulateMonthAsync` kiểu fire-and-forget. Nhiều lần gọi chạy song song có thể ghi đè `Days` lẫn nhau, và exception bị nuốt.
  - Thiếu test tháng thiếu (tháng âm 29 ngày, ngày sau 29 là mùng 1), header en-US, ô ngoài phạm vi 1900–2100, và sự kiện kéo dài nhiều ngày (hiện chỉ gắn vào ngày bắt đầu, `:216`).
- **OK:** test `FR_UI_2_First_lunar_day_shows_day_slash_month` có dùng tháng 2 nhuận 2023 (22/03/2023) và assert đủ `Day`, `Month`, `IsLeapMonth`, text. `FR_LUNAR_6_...` kiểm tra mọi ô đều rỗng khi tắt và hiện lại khi bật. Có test đầu tuần T2/CN theo setting.

## P2-08 · WeekView, DayView

- **Blocker:** múi giờ +7 hard-code (`WeekViewModel.cs:163`, `DayViewModel.cs:130`, `DayColumnViewModel.cs:153`), xem phần "Vấn đề chung".
- **Nên sửa:**
  - `WeekViewModel.cs:201` viết cứng "Tháng". Dòng `:207-208` dùng `{date:MMM d}` theo `CurrentCulture` của thread, không theo `ILocalizationService`.
  - `DayView.xaml:98` viết cứng `'Âm lịch: {0}'`.
- **OK:** có test vị trí theo giờ, chia lane khi chồng giờ, tách sự kiện cả ngày, vạch giờ hiện tại, header có ngày âm.

## P2-09 · AgendaView

- **Blocker:** múi giờ +7 hard-code (`AgendaViewModel.cs:129,198`).
- **Nên sửa:** màu fallback hard-code ở `AgendaViewModel.cs:173,265`.
- **OK:** có test nhóm theo ngày, lọc chip, ẩn ngày âm, cả ngày đứng trước, trạng thái rỗng.

## P2-10 · DayDetailPanel (rủi ro cao)

- **Blocker:**
  - `DayDetailViewModel.cs:229` hard-code `TimeSpan.FromHours(7)` cho cả khoảng truy vấn lẫn giờ hiển thị (`:265`), nên sai với người dùng ở múi giờ khác hoặc khi họ chọn múi khác.
- **Nên sửa:**
  - Chuỗi viết cứng: `:158-159` ("Tháng"), `:204` ("(Leap)"/"(Nhuận)"), `:208-209` ("Month"/"Tháng"). Phải chuyển vào resx.
  - `:241` dùng `catch` rỗng, nuốt cả `OperationCanceledException`.
  - `:265` luôn hiện `HH:mm` và bỏ qua setting 12h/24h.
  - Test `DayDetailViewModelTests.cs:131-134` chỉ assert cờ `IsLunarVisible`. `UpdateLunarDisplay` (`:172`) không xét cờ này và vẫn điền text, nên phải assert thêm text rỗng hoặc kiểm tra binding Visibility.
  - Test gloss `LocalizationTests.cs:347-348, 359-360` chỉ kiểm tra en khác rỗng. Nếu key thiếu trong en, giá trị sẽ fallback về resx neutral (= vi) và test vẫn pass. Cần assert `enVal != viVal` hoặc chứa từ tiếng Anh.
- **OK:** test `FR_UI_3_Day_detail_shows_can_chi_and_tiet_khi` dùng ngày tham chiếu S09 (16/10/2026: Quý Hợi, Mậu Tuất, Bính Ngọ, Hàn lộ 195°) và assert giá trị đúng. Có test gloss en (`Yin Water Pig`, `Cold Dew`) và tháng 6 nhuận 2025 cho cả vi lẫn en.

## P2-11 · Đầu tuần, 12h/24h, múi giờ (rủi ro cao)

- **Blocker:**
  - Setting chỉ được lưu, không được áp dụng. Các event `WeekStartChanged`, `TimeFormatChanged`, `TimezoneChanged`, `LunarEnabledChanged` (`AppearanceSettingsViewModel.cs:24-33`) không có nơi nào subscribe.
  - `TimeFormattingHelper` không được VM nào gọi. Mọi chỗ đều hiện `HH:mm` với offset +7.
  - Trang `AppearancePage` chỉ được đăng ký DI (`App.xaml.cs:38-39`) nhưng không gắn vào UI nào, nên người dùng không mở được S13.
  - "Xong khi" chỉ đạt về mặt hình thức: test lưới T2/CN (`MonthViewModelTests.cs:189`) và test format giờ có pass, nhưng tính năng chưa chạy đầu-cuối.
  - `AppearanceSettingsViewModel.cs:117,132,145,158,169` dùng `async partial void`. Đây không phải event handler nên vi phạm AGENTS.md §7. Exception khi lưu setting sẽ làm crash app.
- **Nên sửa:**
  - `TimeFormattingHelperTests.cs:47-49` assert cho vi 12h quá lỏng (`Contains("02:30")` và `CH || PM`). Nên assert chính xác chuỗi mong đợi, hoặc ghi rõ quyết định dùng `hh` thay vì `h` cho vi.
  - Chưa có test áp dụng múi giờ khác múi hệ thống, ví dụ chọn UTC-5 thì sự kiện 23:30 UTC+7 phải rơi sang ngày khác.
- **OK:** test VM lưu đủ 5 setting và bắn event (`AppearanceSettingsViewModelTests.cs:32-140`).

## P2-12 · FlaUI smoke (rủi ro cao)

- **Blocker:**
  - "Kiểm tra có ngày âm" chưa đạt. `SmokeTests.cs:80` chỉ kiểm tra subtitle khác rỗng, không kiểm tra nội dung âm lịch (ví dụ regex `Âm lịch|Lunar` kèm số), và không kiểm tra ngày âm trong ô tháng hay header tuần.
  - STATUS không ghi kết quả chạy local ("pass local"). `STATUS.md:43` chỉ ghi quyết định chạy trên CI, và lý do nêu ra không có bằng chứng (không có run ID).
- **Nên sửa:**
  - `SmokeTests.cs:123,132,141,148` dùng `Thread.Sleep(500)`, dễ flaky. Nên dùng `Retry.WhileNull`/`Retry.WhileFalse`.
  - Sau khi chuyển view, test không assert view cũ đã biến mất, nên vẫn pass nếu mọi view luôn có trong cây UIA.
  - `SmokeTests.cs:22` ưu tiên bản Debug, nên ở máy local có thể chạy nhầm bản Debug cũ trong khi vừa build Release.
  - `SmokeTests.cs:70,109` có `window.Should().NotBeNull()` thừa, vì `GetMainWindow` đã ném lỗi khi hết thời gian chờ.
- **OK:** có `App_starts_and_shows_main_window` và test chuyển đủ Tháng/Tuần/Ngày/Lịch biểu theo AutomationId. Có `finally` đóng hoặc kill process.

## P2-13 · Kết thúc phase

- **Blocker:** đang phụ thuộc các Blocker ở P2-07, P2-10, P2-11, P2-12. Không xác minh được "CI xanh" vì không có run ID trong STATUS.
- **Nên sửa:** `STATUS.md:41` vẫn ghi kết quả của commit cũ `159781c`.
- **OK:** TRACEABILITY và CHANGELOG có thay đổi trong diff.

---

## Việc cần sửa (giao cho Antigravity)

Thứ tự đề xuất: theo số thứ tự. Mỗi task làm theo quy tắc "1 task một lần" trong AGENTS.md.

- [ ] **P2-R1** · FR-UI-7, FR-I18N-4 · Thêm port `IClock`/`ITimeZoneProvider` (đọc setting `timezone`, mặc định múi hệ thống). Bỏ mọi `TimeSpan.FromHours(7)` (6 chỗ) và cách truy vấn nửa đêm UTC ở `MonthViewModel.cs:210`; chuyển ngày theo `TimeZoneInfo.ConvertTime`. · Xong khi: test `FR_UI_7_Events_bucketed_by_selected_timezone` (chọn UTC-5, sự kiện 23:30 UTC+7 sang ngày khác) cho Month/Week/Day/Agenda/DayDetail; grep `FromHours(7)` trong src/ trả về rỗng.
- [ ] **P2-R2** · FR-UI-7, FR-I18N-4 · Nối `AppearanceSettingsViewModel` vào `MainWindowViewModel`: subscribe 4 event, áp dụng week_start/time_format/timezone/lunar cho mọi view. Bỏ đoạn suy đầu tuần theo culture ở `MainWindowViewModel.cs:289,317`. Dùng `TimeFormattingHelper` cho mọi chuỗi giờ. Gắn `AppearancePage` vào điều hướng Cài đặt. · Xong khi: test VM "đổi week_start rồi đổi ngôn ngữ thì week_start vẫn giữ"; test "đổi 12h thì giờ trong DayDetail/Agenda là `9:00 AM`"; kiểm tra thủ công mở được S13, ghi vào STATUS.
- [ ] **P2-R3** · AGENTS.md §7 · Thay 5 `async partial void` ở `AppearanceSettingsViewModel.cs` bằng `partial void` gọi command async có xử lý lỗi (hoặc `AsyncRelayCommand`). Sửa các lời gọi fire-and-forget `PopulateMonthAsync` (`MonthViewModel.cs:67,93,269`) cho tuần tự hoặc huỷ được (CancellationTokenSource). · Xong khi: test lỗi `ISettingsStore.SetAsync` không làm crash và VM báo lỗi; test gọi `SetWeekStart` hai lần liên tiếp thì `Days` đúng theo lần cuối.
- [ ] **P2-R4** · FR-I18N-3, FR-I18N-4, FR-UI-2 · Chuyển toàn bộ chuỗi viết cứng vào resx (vi + en): "Tháng…", "Th 2..CN", "(Nhuận)/(Leap)", "Month", hậu tố nhuận `n` (`MonthDayViewModel.cs:120`), `MainWindow.xaml:129`, `DayView.xaml:98`, `Sidebar.xaml:175`, `MiniCalendar.xaml:136`. · Xong khi: grep `"[^"]*(Tháng|Nhuận|Leap|lệnh đang chờ|Âm lịch:)` trong `ViewModels/` và `*.xaml` trả về rỗng; test header tháng ở en-US; test `1/2` nhuận ở en theo ký hiệu đã chốt (ghi ADR nếu Figma không quy định).
- [ ] **P2-R5** · FR-UI-7, FR-UI-11 · Thay `FontSize`/`CornerRadius` literal và `Foreground="#000000"` ở các dòng đã liệt kê trong mục P2-03 bằng `{DynamicResource}` token. Đưa màu fallback calendar vào một hằng hoặc brush token duy nhất (gộp `#2F6BFF` và `#4285F4`). · Xong khi: grep `FontSize="[0-9]`, `CornerRadius="[0-9]`, `"#[0-9A-F]{6}"` trong `Views/` và `Controls/` trả về rỗng; đổi theme Tối/HC khi đang chạy không còn chữ đen, ghi kết quả thủ công vào STATUS (đóng luôn mục còn thiếu của P2-03).
- [ ] **P2-R6** · NFR-PERF · Đo render MonthView thật: dừng đồng hồ sau `Dispatcher` render (ví dụ `LayoutUpdated`/`ContentRendered` trong FlaUI hoặc STA test), dùng store có khoảng 200 sự kiện. · Xong khi: STATUS ghi con số cụ thể (min/median/max, cấu hình máy, Debug/Release, số lần lặp) và kết quả < 100 ms.
- [ ] **P2-R7** · FR-UI-2, FR-LUNAR-6, FR-I18N-5 · Siết test: thêm test tháng thiếu 29 ngày và ô ngoài phạm vi 1900–2100 cho MonthView; `DayDetailViewModelTests.cs:131` assert text âm lịch rỗng khi tắt; test gloss assert `en != vi` cho chi và tiết khí; `LocalizationTests.cs:43` assert `vi-VN`; `TimeFormattingHelperTests.cs:47` assert chuỗi chính xác. · Xong khi: các test mới pass và không test nào bị nới lỏng.
- [ ] **P2-R8** · spec §9 · Sửa `SmokeTests.cs`: assert regex ngày âm ở subtitle và ở ít nhất một ô tháng/header tuần; thay `Thread.Sleep` bằng `Retry`; assert view cũ biến mất sau khi chuyển; chọn bản build theo cấu hình của test (không ưu tiên Debug). · Xong khi: test pass local 5 lần liên tiếp và pass trên CI; STATUS ghi kết quả local và run ID CI.
- [ ] **P2-R9** · spec §13 · Sau P2-R1..R8: cập nhật `STATUS.md` (bỏ dòng commit cũ ở `:41`, ghi run ID CI xanh), TRACEABILITY cho các test mới, CHANGELOG. · Xong khi: CI xanh, có run ID trong STATUS.

## Kết quả sửa (Claude Code, 2026-11-01 17:42 +07:00)

Chủ dự án yêu cầu Claude Code sửa luôn thay vì giao Antigravity. Mỗi task một commit, mỗi commit đều pass build Release, format, test và cổng coverage.

| Task | Commit | Kết quả |
| --- | --- | --- |
| P2-R1 múi giờ | `165b601` | `CalendarTime` (Core) + `IDisplayPreferences`; bỏ 6 chỗ +7. Phát hiện thêm: smoke test chạy bản Debug làm cổng coverage Lunar tụt còn 89%; đã sửa |
| P2-R2 setting Giao diện | `71bc871` | Setting áp dụng ngay; đầu tuần không bị ngôn ngữ ghi đè; nút ⚙ mở S13. Phát hiện thêm: S13 crash khi mở (thiếu resource `Loc`) |
| P2-R3 async | `9aacaee` | Bỏ `async partial void` (làm ở R2); nạp lại theo phiên, kết quả cũ bị bỏ; bỏ `catch` rỗng |
| P2-R4 i18n | `8b0e8c5` | `CalendarText` + ~40 key resx; không còn chuỗi UI trong VM/XAML |
| P2-R5 token | `71302de` | Thay hết literal trong `Views/` và cả `Controls/` (review trước bỏ sót `Controls/`); test chặn literal; smoke đổi theme |
| P2-R6 NFR-PERF | `da63264` | Đo trên view thật: trước 132 ms median, **vượt ngưỡng**; sau tái sử dụng ô/chip: ~21 ms median, ~43 ms max |
| P2-R7 test yếu | `da95572` | Thêm test tháng thiếu, ngoài phạm vi; DayDetail xoá giá trị âm lịch khi tắt. Sửa lại đề xuất sai của review: `ResolveDefaultCulture(null)` hỏi OS, không phải luôn vi-VN |
| P2-R8 smoke | `0c3e59a` | Assert nội dung âm lịch, view cũ biến mất, bỏ `Thread.Sleep`; pass 5/5 |
| P2-R9 tài liệu | commit cuối | STATUS, CHANGELOG, TRACEABILITY |

Còn mở: chưa có run CI sau các commit này (chưa push); UI smoke trên CI cần xác nhận lại.

