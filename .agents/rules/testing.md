---
trigger: glob
globs: "tests/**"
description: Quy ước test LunarCal - framework, cách đặt tên, mỗi FR có test, cách mock Google API và Gemini, coverage gate.
---

# Kiểm thử

Nguồn: spec §9 (kiểm thử), §7 (test Gemini), NFR-MAINT, §13 (Definition of Done từng phase). ADR: `docs/adr/0003`, `0004`, `0009`. Bảng đối chiếu: `docs/TRACEABILITY.md`.

## Framework

- xUnit v3 (`xunit.v3` 4.0.2) chạy trên Microsoft.Testing.Platform (bật trong `global.json`: `"test": { "runner": "Microsoft.Testing.Platform" }`). Test project là executable (`OutputType=Exe`). Không dùng `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` hay coverlet (ADR-0004).
- Assertion: `AwesomeAssertions` 9.6.0 (namespace `AwesomeAssertions`, API giống FluentAssertions; ADR-0003). Mock: `NSubstitute` 6.2.0. UI: `FlaUI.UIA3` 5.0.0. Coverage: `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0.
- `using Xunit;` và `using AwesomeAssertions;` đã là global using trong mỗi `.csproj` test.

## Project test

| Project | Đối tượng |
| --- | --- |
| `tests/LunarCal.Core.Tests` (`net10.0`) | Unit test Core. Có `ArchitectureTests` (Core chỉ dùng BCL) và `Lunar/` |
| `tests/LunarCal.Infrastructure.Tests` | Infrastructure: fake `HttpMessageHandler`, storage, security. Có `ArchitectureTests` |
| `tests/LunarCal.App.UiTests` | Smoke test FlaUI, chạy `LunarCal.exe` ở process riêng. Hiện có 1 test skip, chờ Phase 2 |

## Lệnh

```powershell
dotnet test --solution LunarCal.slnx
dotnet test --project tests/LunarCal.Core.Tests
dotnet test --solution LunarCal.slnx --coverage --coverage-output-format cobertura --results-directory TestResults
pwsh -NoProfile -Command "./eng/Test-Coverage.ps1 -Reports 'TestResults/*.cobertura.xml' -Gate 'src/LunarCal.Core/Lunar=95','src/LunarCal.Core=85'; exit `$LASTEXITCODE"
```

Cổng coverage (`eng/Test-Coverage.ps1`, bước "Coverage gate" trong `.github/workflows/ci.yml`): Lunar ≥ 95%, Core ≥ 85% số dòng (NFR-MAINT). Thêm module mới cần cổng riêng thì thêm `path=threshold` vào lệnh trong CI.

## Đặt tên

- Tên test: câu mô tả dùng gạch dưới, có mã yêu cầu ở đầu khi test cover một yêu cầu, ví dụ `FR_LUNAR_3_Tet_falls_on_the_spec_date`, `SEC_KEY_2_Api_key_is_sent_in_header_not_query`. CA1707 đã tắt cho `tests/**` trong `.editorconfig`.
- Class test: `<ClassUnderTest>Tests` hoặc theo chủ đề (`LunarReferenceTests`), đặt trong thư mục giống cấu trúc `src`.
- Mỗi yêu cầu FR-*, SEC-*, NFR-* thuộc phase hiện tại phải có ít nhất một test, hoặc ghi lý do trong `docs/TRACEABILITY.md`. Sau khi thêm test, thêm hoặc cập nhật dòng tương ứng trong TRACEABILITY.

## Fixture

- Đặt ở `tests/<Project>/Fixtures/`, copy ra output qua `<None Include="Fixtures\*.csv" CopyToOutputDirectory="PreserveNewest" />`. Ghi nguồn trong `Fixtures/README.md`.
- Fixture tham chiếu phải đến từ nguồn bên ngoài, không sinh từ chính code đang test. Ví dụ: lịch âm dùng bảng HKO 1901–2100 cùng các mốc trong spec (ADR-0009, `tests/LunarCal.Core.Tests/Fixtures/README.md`).

## Mock Google Calendar API

- Không gọi Google thật trong test tự động.
- Infrastructure: tạo `HttpClient` với một `HttpMessageHandler` giả trả JSON ghi sẵn. Kịch bản bắt buộc (spec §9): `syncToken` incremental, 410 Gone → full resync, 429 và 403 `rateLimitExceeded` → backoff, xung đột ETag (412) → server thắng.
- Nếu dùng `Google.Apis.Calendar.v3`: truyền handler giả qua `HttpClientFactory` của `BaseClientService.Initializer`. Cách làm cụ thể với phiên bản thư viện đang dùng là CHƯA XÁC MINH; đọc tài liệu khi làm task P3-09.
- Core và App: mock `ICalendarGateway` hoặc `IEventStore` bằng NSubstitute.

## Mock Gemini

- `IChatModelClient` (Core) mock bằng NSubstitute khi test `ChatOrchestrator` và validator.
- `GeminiChatModelClient` (Infrastructure) test bằng fake `HttpMessageHandler` với phản hồi ghi sẵn (spec §7): function call hợp lệ, sai schema, 429, 401/403, bị chặn an toàn, timeout. Assert thêm rằng key nằm ở header `x-goog-api-key` và không có trong URL (SEC-KEY-2).
- Golden test (câu tiếng Việt và tiếng Anh → tool call mong đợi) chạy thủ công với key thật, không chạy trong CI. Đánh dấu bằng trait hoặc skip mặc định, và ghi cách chạy trong README của project test.

## Các loại test bắt buộc khác (spec §9)

- Migration: test nâng cấp từ mọi version cũ.
- SecretStore: round-trip. LogRedactor: các mẫu `AIza…`, `ya29…`, `Bearer …`, `refresh_token`.
- Prompt injection mẫu: app không được thực thi (SEC-PI-4).
- Quét log và lake sau một kịch bản đầy đủ: không có chuỗi giống key hoặc token.
- FR-NOTIFY: danh sách kịch bản ở spec §9 mục "Nhắc nhở".
- i18n: mọi key có đủ vi và en, không rỗng (FR-I18N-3).
- UI smoke (FlaUI): mở app, chuyển view, mở thanh lệnh, hiển thị ngày âm. Dùng `AutomationProperties.AutomationId` (cửa sổ chính có `MainWindow`).

## Không được

- Xoá, skip hay làm yếu một test đang fail để cho pass. Hãy sửa code, hoặc ghi vào `docs/STATUS.md`.
- Test phụ thuộc thời gian thực hoặc mạng thật (dùng `IClock`, handler giả). Test hiệu năng phải có ngưỡng rộng và warm-up (xem `tests/LunarCal.Core.Tests/Lunar/LunarPerformanceTests.cs`).
