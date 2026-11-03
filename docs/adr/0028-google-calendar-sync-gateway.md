# ADR-0028: Google Calendar API v3 Sync Gateway and SyncEngine

## Trạng thái

Accepted (2026-11-03).

## Bối cảnh

- LunarCal là ứng dụng desktop Windows quản lý lịch Google Calendar với khả năng làm việc offline (spec §1, §4.2).
- Theo yêu cầu **FR-SYNC-1**, lần đồng bộ đầu tiên cần:
  1. Lấy danh sách lịch của người dùng (`users/me/calendarList`).
  2. Lấy các sự kiện (`calendars/{calendarId}/events`) với tham số `singleEvents=true` trong cửa sổ thời gian từ −12 tháng đến +24 tháng tính từ thời điểm hiện tại (`timeMin`, `timeMax` định dạng RFC 3339).
- Theo yêu cầu **FR-SYNC-6**, ứng dụng phải lưu trữ vào cơ sở dữ liệu SQLite: `eventId` (Google event ID), `recurringEventId` (ID của chuỗi lặp nếu là instance), `etag` (dùng cho optimistic concurrency control ở FR-SYNC-4), `updated` (thời điểm cập nhật cuối), và toàn bộ từ điển `extendedProperties.private` (lưu dưới dạng JSON vào cột `extended_json` để bảo tồn các trường như `lunarRuleId`, `createdBy=lunarcal-chat`, `batchId`, `requestId`).
- Cần tuân thủ quy tắc bảo mật mạng **SEC-NET-1** và **SEC-NET-2** (pipeline `HttpClient` của P3-08, token refresh tự động P3-06, DPAPI store P3-02).

## Quyết định

1. **Giao thức và Gateway:**
   - Xây dựng `ICalendarGateway` (trong `LunarCal.Core.Abstractions`) và hiện thực `GoogleCalendarGateway` (trong `LunarCal.Infrastructure.Google`).
   - Sử dụng REST API trực tiếp trên `HttpClient` có gắn pipeline bảo mật và resilience (SEC-NET-1, SEC-NET-2, ADR-0017, ADR-0026), không phụ thuộc vào thư viện Google.Apis client nặng nề, giúp kiểm soát tối đa vòng đời HTTP, phân tích JSON hiệu năng cao với `System.Text.Json`, xử lý mã lỗi và token refresh trong suốt.
2. **Cửa sổ thời gian và Single Events (FR-SYNC-1):**
   - Với lần sync đầu tiên (chưa có `syncToken`):
     - Gửi request `GET https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events` với:
       - `singleEvents=true` (Google tự mở rộng RRULE thành các instance độc lập).
       - `timeMin`: thời điểm hiện tại trừ 12 tháng (UTC, định dạng `yyyy-MM-dd'T'HH:mm:ss'Z'`).
       - `timeMax`: thời điểm hiện tại cộng 24 tháng (UTC, định dạng `yyyy-MM-dd'T'HH:mm:ss'Z'`).
     - Lặp phân trang qua `pageToken` cho đến khi hết trang.
     - Trang cuối cùng trả về `nextSyncToken`; token này được lưu vào bảng `CalendarInfo.sync_token` phục vụ incremental sync ở task P3-10.
3. **Ánh xạ dữ liệu sự kiện (FR-SYNC-6, spec §6):**
   - `id`: Google ID được lưu vào cột `google_id`. Cột khóa chính `id` là khóa định danh duy nhất (có thể dùng chính Google ID hoặc GUID duy nhất).
   - `start`/`end`: Xử lý cả sự kiện có giờ (`dateTime`) và sự kiện cả ngày (`date` định dạng `yyyy-MM-dd`). Với sự kiện cả ngày, `all_day = 1`, `start_utc` là đầu ngày và `end_utc` là cuối ngày hoặc exclusive end date.
   - `recurringEventId`: lưu trực tiếp từ trường `recurringEventId` của Google.
   - `etag`: lưu nguyên bản ETag của sự kiện.
   - `updated`: parse `updated` RFC 3339 sang `DateTimeOffset` UTC.
   - `extendedProperties.private`: serialize dictionary thành chuỗi JSON lưu vào `extended_json`.
4. **Động cơ đồng bộ (SyncEngine):**
   - Lớp `SyncEngine` điều phối luồng: lấy access token hợp lệ qua `IGoogleOAuthClient` (tự động refresh nếu cần), gọi `ICalendarGateway` lấy danh sách calendar và sự kiện của từng calendar được chọn, sau đó ghi đồng thời vào SQLite bằng `ISqliteConnectionFactory` trong một database transaction duy nhất cho mỗi calendar để đảm bảo tính toàn vẹn dữ liệu.

## Nguồn tài liệu tham khảo (đọc ngày 2026-11-03)
- Google Calendar API v3 CalendarList: `https://developers.google.com/calendar/api/v3/reference/calendarList/list`
- Google Calendar API v3 Events: `https://developers.google.com/calendar/api/v3/reference/events/list`
- Google Calendar Sync Guide: `https://developers.google.com/calendar/api/guides/sync`
