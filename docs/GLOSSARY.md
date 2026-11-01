# GLOSSARY: thuật ngữ và quy tắc tính lịch âm

Thuật ngữ nghiệp vụ vi ↔ en, kèm tên dùng trong code. Nguồn sự thật cho chuỗi giao diện là resx và bảng ở spec §4.11, không phải file này.

## Lịch âm

| Tiếng Việt | English | Trong code / ghi chú |
| --- | --- | --- |
| Âm lịch | Lunar calendar (Vietnamese lunisolar calendar) | `LunarCal.Core.Lunar` |
| Dương lịch | Solar (Gregorian) calendar | `DateOnly` |
| Ngày âm | Lunar date | `LunarDate(Year, Month, Day, IsLeapMonth)` |
| Năm âm | Lunar year | `LunarDate.Year`: năm dương mà tháng 1 âm của nó bắt đầu |
| Tháng nhuận | Leap month | `IsLeapMonth`, `LunarCalendar.GetLeapMonth(year)` (0 nếu không có) |
| Tháng đủ / tháng thiếu | Long (30-day) / short (29-day) month | `LunarCalendar.GetDaysInMonth` |
| Mùng một (mồng 1) | First day of the lunar month | Ngày có điểm sóc. UI hiển thị dạng `1/tháng` (FR-UI-2) |
| Rằm | 15th day of the lunar month (full-moon day) | `Day == 15` |
| Sóc | New moon (conjunction) | `Astronomy.NewMoon(k)` (internal) |
| Tết Nguyên Đán | Lunar New Year (Tết) | Ngày 1 tháng 1 âm, không nhuận |
| Giỗ | Death anniversary (memorial day) | Thường là luật âm lịch hằng năm (`LunarRule`, FR-LUNAR-4) |
| Can chi | Sexagenary (stem-branch) cycle | `CanChi(ThienCan Can, DiaChi Chi)`, `CycleIndex` 0 = Giáp Tý … 59 = Quý Hợi |
| Thiên can | Heavenly stem | `ThienCan`: Giáp, Ất, Bính, Đinh, Mậu, Kỷ, Canh, Tân, Nhâm, Quý |
| Địa chi | Earthly branch | `DiaChi`: Tý, Sửu, Dần, Mão, Thìn, Tỵ, Ngọ, Mùi, Thân, Dậu, Tuất, Hợi. Tiếng Việt dùng Mão (Mèo) và Sửu (Trâu), khác tiếng Trung (Rabbit, Ox) |
| Tiết khí | Solar term (24 per year) | `SolarTerm`, `TietKhi(Term, IsStartDay)` |
| Trung khí | Major solar term (12, ở các kinh độ bội số của 30°) | Dùng để xác định tháng nhuận |
| Tuế (suì) | Winter-solstice year (từ tháng 11 tới tháng 11 năm sau) | `LunarYearTable` (internal) |
| Ngũ hổ độn | Rule giving the stem of month 1 (Dần) from the year stem | `GetMonthCanChi` |
| Luật lặp âm lịch | Lunar recurrence rule | `LunarRule` (Phase 5): một ngày âm mỗi tháng hoặc mỗi năm |
| Chính sách tháng nhuận | Leap-month policy | Mặc định chỉ tháng thường; tuỳ chọn tính cả tháng nhuận (FR-LUNAR-4) |
| Vật chất hoá luật | Rule materialization | Đổi luật âm lịch thành các ngày dương trong N năm tới (mặc định 5, tối đa 20) |
| Âm lịch · cục bộ | Lunar · local (virtual calendar) | Calendar ảo, mặc định không đồng bộ lên Google (FR-LUNAR-5) |
| Hiển thị Âm lịch | Show lunar calendar | Setting `lunar_enabled`, công tắc tổng (FR-LUNAR-6) |

## Lịch, đồng bộ, nhắc nhở

| Tiếng Việt | English | Trong code / ghi chú |
| --- | --- | --- |
| Sự kiện | Event | `CalendarEvent` (Phase 2), bảng `Event` |
| Lần (của sự kiện lặp) | Occurrence (instance) | Khoá `(event_id, occurrence_start)` trong `ReminderState`; sync dùng `singleEvents=true` |
| Sự kiện lặp | Recurring event | RRULE theo RFC 5545 (`RecurrenceHelpers`) |
| Sự kiện cả ngày | All-day event | `all_day` |
| Lịch đích | Target calendar | `calendarId` |
| Nhắc nhở | Reminder | `event.reminders` (chỉ `popup`), `LunarRule.reminders` (FR-NOTIFY-1) |
| Hoãn | Snooze | `snoozed_until` (FR-NOTIFY-6) |
| Hoàn thành | Done / completed | `completed_at`, chỉ cho một lần, chỉ lưu cục bộ (FR-NOTIFY-7) |
| Nhắc lỡ | Missed reminder | FR-NOTIFY-3 |
| Thông báo (toast) | Toast notification | Chuỗi `Toast_*` (spec §4.11) |
| AUMID | App User Model ID | Cố định, không phụ thuộc tên thương mại (FR-NOTIFY-10) |
| Đồng bộ tăng dần | Incremental sync | `syncToken`, 410 Gone → full resync (FR-SYNC-2) |
| Hàng đợi ghi offline | Offline mutation queue | Bảng `PendingMutation` (FR-SYNC-4) |
| Xung đột | Conflict | ETag / If-Match, server thắng |

## AI và dữ liệu

| Tiếng Việt | English | Trong code / ghi chú |
| --- | --- | --- |
| Thanh lệnh AI | AI command bar | FR-UI-5 |
| Lệnh AI | AI command | Bảng `AiCommand`, mã hiển thị `#1000` |
| Hàng chờ AI | AI queue | Sáu trạng thái: Chờ xử lý (Pending), Đang xử lý (Processing), Chờ xác nhận (Awaiting confirmation), Lỗi (Error), Hoàn tất (Completed), Đã huỷ (Cancelled) (FR-QUEUE-2). Tên tiếng Anh là đề xuất, CHƯA XÁC MINH |
| Thời điểm gõ | Typed-at time | `typed_at`, mốc để hiểu "ngày mai", "tuần sau" (FR-QUEUE-4) |
| Ngôn ngữ lệnh | Input language | `input_lang`: vi, en, mixed |
| Hộp thoại xác nhận | Confirmation dialog | FR-CONFIRM |
| Độ tin cậy | Confidence | Heuristic cục bộ: ≥ 90% Rõ ràng, 60–89% Cần kiểm tra, < 60% Không chắc (FR-CONFIRM-4) |
| Lô / hoàn tác | Batch / undo | `batchId` và `createdBy=lunarcal-chat` trong `extendedProperties.private` (FR-CHAT-5) |
| Mã yêu cầu (idempotency) | Request ID | `requestId` trong `extendedProperties.private` (FR-QUEUE-8) |
| BYOK | Bring your own key | Key Gemini của người dùng, lưu bằng DPAPI (FR-BYOK) |
| Đồng ý tham gia | Opt-in / consent | `training_opt_in`, `consent_version`, `consent_lang` |
| Đã loại thông tin nhận dạng | De-identified | Chuỗi UI bắt buộc thay cho "ẩn danh" (FR-I18N-7) |
| Giả danh hoá | Pseudonymized | Dữ liệu còn `contributor_id` (FR-CLOUD-3) |
| Outbox | Outbox | JSONL mã hoá chờ upload (FR-DATA-2) |
| Người đóng góp | Contributor | `contributor_id`, UUID ngẫu nhiên |
| Bia mộ (xoá) | Tombstone | Đánh dấu record đã xoá để không nạp lại (FR-CLOUD-4) |

## Quy tắc tính lịch âm mà code đang dùng

Code: `src/LunarCal.Core/Lunar/` (`Astronomy.cs`, `LunarYearTable.cs`, `LunarCalendar.cs`). Spec: §4.4 FR-LUNAR-1..3. ADR: 0006, 0007, 0008, 0009.

1. **Thuật toán Hồ Ngọc Đức**, múi giờ cố định **UTC+7** cho toàn bộ phạm vi (kể cả trước 1968).
2. **Phạm vi:** dương lịch 1900-01-01..2100-12-31; năm âm 1899..2100 cho các hàm đổi âm sang dương. Ngoài phạm vi thì ném `ArgumentOutOfRangeException`. Ngày âm hợp lệ về cú pháp nhưng không tồn tại (tháng nhuận năm đó không có, ngày 30 của tháng thiếu) thì `ToSolar` trả `null`.
3. **Ngày mùng 1** là ngày dương (giờ UTC+7) chứa điểm sóc. Sóc tính theo chuỗi Meeus rút gọn có hiệu chỉnh ΔT.
4. **Tháng 11 âm** là tháng chứa Đông chí (kinh độ Mặt Trời 270°).
5. **Năm nhuận:** giữa hai tháng 11 liên tiếp có 13 tháng (hai điểm bắt đầu cách nhau hơn 365 ngày). **Tháng nhuận** là tháng đầu tiên sau tháng 11 không chứa trung khí nào. Tháng nhuận mang số của tháng ngay trước nó. Có thể nhuận tháng 11 hoặc 12 (ví dụ năm 2033 nhuận tháng 11).
6. **Năm âm của tháng:** tháng 11 và 12 nằm ở đầu một tuế thuộc năm âm trước; các tháng còn lại thuộc năm âm mới.
7. **Độ dài tháng** = số ngày giữa hai lần sóc liên tiếp: 29 (thiếu) hoặc 30 (đủ).
8. **Can chi ngày** (JDN là số ngày Julius): can = (JDN + 9) mod 10, chi = (JDN + 1) mod 12. Chu kỳ 60 ngày liên tục, không phụ thuộc múi giờ.
9. **Can chi tháng:** chi = (tháng + 1) mod 12 (tháng 1 = Dần); can = (năm × 12 + tháng + 3) mod 10 (ngũ hổ độn). **Tháng nhuận dùng can chi của tháng thường cùng số.**
10. **Can chi năm:** can = (năm + 6) mod 10, chi = (năm + 8) mod 12. Ví dụ 1984 = Giáp Tý, 2026 = Bính Ngọ.
11. **Tiết khí của một ngày** = ⌊kinh độ Mặt Trời / 15°⌋, đo tại nửa đêm (UTC+7) kết thúc ngày đó; `IsStartDay` đúng nếu tiết khí đổi trong ngày. Công thức kinh độ không có hiệu chỉnh nutation và aberration (sai số khoảng 0,01°, tức khoảng 15–25 phút); cùng một công thức được dùng cho trung khí khi xác định tháng nhuận, để tiết khí hiển thị và tháng nhuận luôn nhất quán.
12. **Cache:** bảng tháng của mỗi tuế được tính một lần rồi lưu cache (`ConcurrentDictionary`); một lưới tháng 42 ô mất dưới 5 ms (FR-LUNAR-2).
13. **Đối chiếu:** so với bảng HKO (UTC+8) 1901–2100, chỉ chấp nhận chênh lệch do múi giờ: 77 đầu tháng sớm hơn một ngày, 13 đầu tháng khác vị trí nhuận (1917, 1922, 1938, 1984–85, 1987), 239 tiết khí sớm hơn một ngày. Chi tiết: `tests/LunarCal.Core.Tests/Fixtures/README.md`.
