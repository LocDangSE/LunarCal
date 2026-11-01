---
trigger: model_decision
description: Kiến trúc LunarCal - các lớp Core/Infrastructure/App, luồng dữ liệu (Google sync, lịch âm, Gemini, data lake), DI và nơi đặt code mới. Đọc khi thêm class, project, service hoặc khi không biết đặt code ở đâu.
---

# Kiến trúc

Nguồn: spec §2 (quyết định kỹ thuật), §3 (cấu trúc solution), §6 (DB), §7 (Gemini), §4.10 (cloud). ADR: `docs/adr/0002`, `0010`, `0013`, `0015`, `0018`.

## Các lớp và quy tắc phụ thuộc

`LunarCal.App → LunarCal.Infrastructure → LunarCal.Core`.

| Lớp | TFM | Được phép | Không được phép |
| --- | --- | --- | --- |
| `src/LunarCal.Core` | `net10.0` | Chỉ BCL. Domain, quy tắc nghiệp vụ, interface (port) | IO, HTTP, WPF, Windows API, package NuGet. `tests/LunarCal.Core.Tests/ArchitectureTests.cs` sẽ fail nếu vi phạm |
| `src/LunarCal.Infrastructure` | `net10.0-windows10.0.19041.0` | Hiện thực port: Google, Gemini, SQLCipher, DPAPI, data lake | Tham chiếu WPF hoặc App (`tests/LunarCal.Infrastructure.Tests/ArchitectureTests.cs`) |
| `src/LunarCal.App` | `net10.0-windows10.0.19041.0` | WPF view, view model, control, resource, DI composition root | Logic nghiệp vụ (đặt vào Core), gọi HTTP trực tiếp (đi qua port) |
| `src/LunarCal.Tools` | Windows TFM | CLI data lake (spec §3, tuỳ chọn) | |

## Nơi đặt code mới

| Loại code | Thư mục |
| --- | --- |
| Lịch âm: chuyển đổi, can chi, tiết khí (xong); `LunarRule` và vật chất hoá (FR-LUNAR-4, 5) | `src/LunarCal.Core/Lunar/` |
| Model sự kiện, calendar, RRULE | `src/LunarCal.Core/Calendar/` (`CalendarEvent`, `CalendarInfo`, `RecurrenceHelpers`) |
| Tool definitions, validator tool call, state machine xác nhận, domain hàng chờ AI | `src/LunarCal.Core/Chat/` (FR-CHAT, FR-CONFIRM, FR-QUEUE) |
| Interface (port) | `src/LunarCal.Core/Abstractions/`: `ICalendarGateway`, `IChatModelClient`, `ISecretStore`, `IEventStore`, `IDataCollector`, `IClock`, `IContributionUploader` |
| OAuth, CalendarGateway, SyncEngine | `src/LunarCal.Infrastructure/Google/` |
| Client REST Gemini | `src/LunarCal.Infrastructure/Gemini/` |
| DPAPI secret store, LogRedactor, KeyValidator | `src/LunarCal.Infrastructure/Security/` |
| SQLCipher, migration | `src/LunarCal.Infrastructure/Storage/` |
| Outbox, consent, uploader | `src/LunarCal.Infrastructure/DataLake/` |
| View / ViewModel / Control | `src/LunarCal.App/Views/`, `ViewModels/`, `Controls/` |
| Theme, style | `src/LunarCal.App/Resources/` |
| resx | `src/LunarCal.App/Localization/` (`.agents/rules/i18n.md`) |
| System prompt có version | `Prompts/system.v1.md` (spec §7). Vị trí project CHƯA XÁC MINH: chốt ở task P4-05 |
| Backend ingestion và IaC | `infra/` (FR-CLOUD-1), project backend mới chốt ở Phase 6b |

Mỗi module có `README.md` ngắn (NFR-MAINT). Khi thêm module thì thêm README.

## DI và cấu hình

- Composition root: `src/LunarCal.App/App.xaml.cs` dựng `Host.CreateApplicationBuilder`, gọi `AddLunarCalInfrastructure(builder.Configuration)` (`src/LunarCal.Infrastructure/DependencyInjection.cs`), rồi đăng ký view model và window.
- Service của Infrastructure đăng ký trong `AddLunarCalInfrastructure`. Không đăng ký rải rác trong App.
- Cấu hình dùng Options pattern, đọc section `LunarCal:*` trong `src/LunarCal.App/appsettings.json`. File này chỉ chứa giá trị không bí mật (spec §11). Giá trị người dùng đổi được thì lưu trong bảng `AppSetting` (spec §6).
- HTTP: dùng `IHttpClientFactory` cùng `Microsoft.Extensions.Http.Resilience` (retry, circuit breaker, timeout), allow-list host theo `LunarCal:Network:AllowedHosts` (SEC-NET-1, 2).
- Thời gian: lấy qua `IClock`, không gọi `DateTime.Now` trong Core, để test được.
- Hosted service (sync engine, hàng chờ AI, scheduler nhắc nhở) chạy trong Generic Host; `App.xaml.cs` hiện chưa start host vì chưa có hosted service nào.

## Luồng dữ liệu

1. **Google sync** (spec §4.1, §4.2): OAuth PKCE loopback → token được lưu qua `ISecretStore` → `SyncEngine` lấy calendar list và events (`singleEvents=true`, cửa sổ −12/+24 tháng) → ghi vào SQLCipher (`CalendarInfo`, `Event`) → UI đọc từ cache. Ghi (tạo/sửa/xoá): optimistic UI → `PendingMutation` → gọi API kèm ETag/If-Match → server thắng khi xung đột (FR-SYNC-4). Incremental dùng `syncToken`; gặp 410 thì full resync.
2. **Lịch âm** (spec §4.4): `LunarCalendar` (static, thread-safe, cache theo năm) được App gọi trực tiếp để hiển thị. `LunarRule` (Phase 5) mặc định lưu cục bộ trong calendar ảo "Âm lịch · cục bộ"; tuỳ chọn đồng bộ thành event đơn lẻ trên Google, gắn `extendedProperties.private.lunarRuleId` (FR-LUNAR-5).
3. **Lệnh AI** (spec §4.6, §4.8, §4.9, §7): thanh lệnh → lưu ngay vào `AiCommand` (kèm `typed_at`, `input_lang`) → worker gọi `IChatModelClient` (chỉ gửi câu lệnh, tên calendar, locale, mốc thời gian; không gửi nội dung event) → validate schema và chính sách → validator cục bộ (xung đột, quá khứ) → hộp thoại xác nhận → người dùng xác nhận → `ICalendarGateway` → ghi `batchId` và `requestId` vào `extendedProperties.private`.
4. **Data lake** (spec §4.7, §4.10): chỉ khi opt-in. Hành động của người dùng → ẩn danh hoá trên máy → outbox JSONL mã hoá → `IContributionUploader` gửi theo lô → Ingestion API → Postgres và Parquet. Chi tiết: `.agents/rules/data-lake.md`.
5. **Nhắc nhở** (spec §4.12): tính nguồn nhắc từ cache và `LunarRule.reminders` → hẹn giờ trong app (48 giờ) và `ScheduledToastNotification` (24 giờ) → trạng thái lưu ở `ReminderState`.

## Mô hình dữ liệu

Bảng tối thiểu và các cột: spec §6. Mọi dữ liệu theo tài khoản đều có `AccountId` ngay từ đầu (FR-AUTH-5). Migration có version, chạy khi khởi động, có test nâng cấp. Ghi theo transaction (NFR-REL).
