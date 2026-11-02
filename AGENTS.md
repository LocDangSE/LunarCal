# AGENTS.md: LunarCal

Hướng dẫn chung cho mọi agent (Gemini/Antigravity, Claude Code, ...). Đây là nguồn hướng dẫn duy nhất; `CLAUDE.md` chỉ import file này.

## 1. Dự án

- LunarCal (tên đã chốt ngày 2026-11-01, ADR-0023): app desktop Windows để xem và sửa Google Calendar, dùng offline được.
- Có lịch âm Việt Nam tính offline: ngày âm, tháng nhuận, can chi, tiết khí.
- Có thanh lệnh AI dùng Gemini, theo mô hình BYOK: người dùng tự nhập API key, key chỉ nằm trên máy.
- Mọi lệnh AI đều qua hộp thoại xác nhận trước khi ghi lịch. Hàng chờ AI giúp lệnh vẫn được lưu khi offline.
- Dữ liệu lệnh chỉ được gửi lên cloud để huấn luyện khi người dùng opt-in. Giao diện song ngữ vi-VN / en-US.

## 2. Thứ tự đọc tài liệu

1. `AGENTS.md` (file này).
2. `docs/STATUS.md`: trạng thái hiện tại, task đang dở (theo từng agent), câu hỏi mở. Sau đó đọc 20 dòng cuối `docs/PROGRESS_LOG.md` để biết agent khác vừa làm gì.
3. `docs/TASKS.md`: backlog, chọn task tiếp theo.
4. `docs/SPEC.md`: đặc tả, nguồn sự thật duy nhất. Chỉ đọc các mục liên quan tới task (ví dụ §4.4 cho lịch âm, §6 cho DB).
5. Rule theo chủ đề trong `.agents/rules/` (architecture, security, progress-sync, review, i18n, testing, data-lake, ui) và ADR trong `docs/adr/` (mục lục ở `docs/DECISIONS.md`).

## 3. Tech stack (phiên bản chính xác)

| Thành phần | Phiên bản / ghi chú |
| --- | --- |
| .NET SDK | 10.0.403, ghim trong `global.json` (`rollForward: latestFeature`). Cài bằng `dotnetup install 10` |
| TFM | Core: `net10.0`. Các project khác: `net10.0-windows10.0.19041.0` (ADR-0002) |
| UI | WPF + MVVM `CommunityToolkit.Mvvm` 8.4.2 (ADR-0010). Thư viện style chưa chốt (ADR-0011) |
| Host/DI | `Microsoft.Extensions.Hosting` 10.0.14, `...Configuration.Abstractions` 10.0.14, `...DependencyInjection.Abstractions` 10.0.14 |
| Test | `xunit.v3` 4.0.2 trên Microsoft.Testing.Platform, `AwesomeAssertions` 9.6.0, `NSubstitute` 6.2.0, `FlaUI.UIA3` 5.0.0, `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 |
| Solution | `LunarCal.slnx` (ADR-0005), Central Package Management (`Directory.Packages.props`), lock file `packages.lock.json` cho từng project |
| Chưa thêm (đã chốt ở spec §2, phiên bản CHƯA XÁC MINH) | Serilog, Google.Apis.Calendar.v3, Google.Apis.Auth, Microsoft.Extensions.Http.Resilience, Microsoft.Data.Sqlite + SQLitePCLRaw.bundle_e_sqlcipher, Parquet.Net, DuckDB, H.NotifyIcon.Wpf, Microsoft.Toolkit.Uwp.Notifications |

## 4. Lệnh (đã chạy thử thành công ngày 2026-10-31)

Chạy trong PowerShell, tại gốc repo `D:\Personal-Projects\GCalendar` hoặc nơi bạn clone repo.

```powershell
dotnet restore LunarCal.slnx                          # CI dùng: dotnet restore LunarCal.slnx --locked-mode
dotnet build LunarCal.slnx                            # warnings là errors (TreatWarningsAsErrors)
dotnet build LunarCal.slnx -c Release
dotnet test --solution LunarCal.slnx                  # 71 test: 70 pass, 1 skip (UI smoke, chờ Phase 2)
dotnet test --project tests/LunarCal.Core.Tests       # chỉ test Core
dotnet format LunarCal.slnx --verify-no-changes       # kiểm tra format (cổng CI)
dotnet format LunarCal.slnx                           # tự sửa format
dotnet run --project src/LunarCal.App                 # mở cửa sổ "LunarCal" (hiện còn trống)

# Coverage + cổng coverage (giống bước CI "Coverage gate")
dotnet test --solution LunarCal.slnx --coverage --coverage-output-format cobertura --results-directory TestResults
pwsh -NoProfile -Command "./eng/Test-Coverage.ps1 -Reports 'TestResults/*.cobertura.xml' -Gate 'src/LunarCal.Core/Lunar=95','src/LunarCal.Core=85'; exit `$LASTEXITCODE"
```

- Test project là executable (`OutputType=Exe`). Không dùng `Microsoft.NET.Test.Sdk` hay VSTest (ADR-0004).
- Sau khi thêm hoặc đổi package: sửa phiên bản trong `Directory.Packages.props`, chạy `dotnet restore LunarCal.slnx`, rồi commit cả các file `packages.lock.json` đã thay đổi.
- CI: `.github/workflows/ci.yml` (windows-latest), chạy restore locked → build Release → format → test + coverage → coverage gate → kiểm tra package có lỗ hổng.

## 5. Cấu trúc repo

Quy tắc phụ thuộc: `App → Infrastructure → Core`. Core chỉ tham chiếu BCL (có test kiểm tra). Infrastructure không tham chiếu WPF hay App (có test kiểm tra).

| Đường dẫn | Vai trò |
| --- | --- |
| `src/LunarCal.Core/` | Domain thuần, `net10.0`. `Lunar/` (engine lịch âm, đã xong), `Calendar/`, `Chat/`, `Abstractions/` (các port như `ISecretStore`, `IChatModelClient`, ...) |
| `src/LunarCal.Infrastructure/` | Hiện thực các port: `Google/`, `Gemini/`, `Security/`, `Storage/`, `DataLake/`. Đăng ký DI trong `DependencyInjection.cs` |
| `src/LunarCal.App/` | WPF: `App.xaml.cs` (Generic Host), `Views/`, `ViewModels/`, `Controls/`, `Resources/` (theme), `Localization/` (resx), `appsettings.json` (chỉ chứa giá trị không bí mật) |
| `src/LunarCal.Tools/` | CLI tuỳ chọn cho data lake (compact/query/export), hiện là placeholder |
| `tests/LunarCal.Core.Tests/` | Unit test Core; `Lunar/` có fixture tham chiếu trong `Fixtures/` (nguồn ghi ở `Fixtures/README.md`) |
| `tests/LunarCal.Infrastructure.Tests/` | Test Infrastructure (fake HttpMessageHandler, ...) |
| `tests/LunarCal.App.UiTests/` | Smoke test UI bằng FlaUI, chạy `LunarCal.exe` ở process riêng |
| `eng/Test-Coverage.ps1` | Cổng coverage cho CI |
| `docs/` | `SPEC.md`, `STATUS.md`, `TASKS.md`, `PROGRESS_LOG.md`, `TRACEABILITY.md`, `GLOSSARY.md`, `THREAT_MODEL.md`, `DATA_LAKE.md`, `adr/`, `reviews/` (kết quả review từng task) |
| `.agents/rules/`, `.agents/workflows/`, `.agents/skills/` | Rule, quy trình và skill (gọi bằng slash command) cho agent |
| `.claude/skills/` | Wrapper slash command cho Claude Code, trỏ tới cùng file trong `.agents/workflows/` (ADR-0022) |

## 6. Quy tắc bắt buộc

- **Spec là nguồn sự thật** (`docs/SPEC.md`). Gặp mâu thuẫn hoặc thiếu đặc tả: ghi vào `docs/STATUS.md` mục "Câu hỏi mở" theo mẫu ở `.agents/workflows/spec-gap.md`, chọn phương án an toàn nhất nếu buộc phải đi tiếp, và ghi ADR nếu đó là quyết định kỹ thuật. Không tự bịa yêu cầu, không tự sửa spec.
- Chỉ hỏi chủ dự án những mục ở spec §14. Các điểm mơ hồ khác: ghi ADR và chọn phương án an toàn nhất.
- **Phân vai (ADR-0025, ADR-0027):** Antigravity là người thực thi: làm lần lượt mọi task của một phase trên branch `task/<ID>` tách từ `phase/<N>`, xong task nào thì merge vào `phase/<N>` và đặt `[x]`. Xong hết phase thì chuyển cổng `P<N>-REVIEW` sang `[r]`. Claude Code là reviewer và người sửa bug: review **cả phase** (`git diff main...phase/<N>`), sửa bug trên `phase/<N>`, rồi đặt cổng `[x]` và merge vào `main`; không review từng task, không nhận task tính năng (chỉ task 👤 hoặc task chủ dự án giao trực tiếp). Chi tiết, trạng thái và cách trả lại: `.agents/rules/progress-sync.md`. Cách review: `.agents/rules/review.md`.
- **Cổng review phase là bắt buộc:** không task nào của phase N+1 được bắt đầu khi `P<N>-REVIEW` chưa `[x]` (trừ task 👤 thiết kế hoặc trả lời spec §14).
- **Mỗi lần chỉ làm 1 task** trong `docs/TASKS.md`. Mỗi lúc chỉ một agent được sửa một branch; người giữ branch có tên ở cuối dòng task.
- **Đồng bộ tiến độ giữa Claude Code và Antigravity** theo `.agents/rules/progress-sync.md` (ADR-0022, ADR-0025). Mỗi lần nhận, bàn giao, dừng hoặc xong task phải ghi nối tiếp một dòng vào `docs/PROGRESS_LOG.md`. Không đụng vào task `[~]` hay cổng `[f]` do agent khác giữ. Đọc lại các file tài liệu dùng chung ngay trước khi sửa.
- Làm theo thứ tự phase ở spec §13. Không bắt đầu phase mà màn hình của phase đó chưa có thiết kế (spec §13 "Điều chỉnh lộ trình", §15.4).
- **Kiểm thử giao diện với Figma (Bắt buộc cho Antigravity):** Đối với các task có sửa, cập nhật hoặc dựng giao diện (View, ViewModel, Control, Style, Token, XAML): Antigravity phải khởi động app (bằng `dotnet run --project src/LunarCal.App` và/hoặc FlaUI UI tests) để kiểm tra trực tiếp trên app đang chạy và so sánh với frame Figma nguồn. Giao diện phải khớp 100% với Figma (bố cục, token màu sắc, typography, spacing, radius, icon SVG, trạng thái rỗng/đang tải) thì mới được pass và đánh dấu hoàn thành.
- **Commit message bằng tiếng Anh, có mã yêu cầu**, ví dụ `feat(lunar): add LunarRule materialization (FR-LUNAR-4)`. Mỗi phase kết thúc bằng một commit/PR riêng, và phải pass build, test, format.
- **Sau mỗi task, hoặc khi phiên sắp kết thúc (hết usage, hết thời gian, bị ngắt), PHẢI cập nhật `docs/STATUS.md`**: ghi đè thành ảnh chụp mới, không ghi nối tiếp, nhưng giữ nguyên mục của agent khác trong "Đang làm theo agent". Nếu task đang dở, ghi rõ đã làm tới đâu, file nào đang sửa dở và bước tiếp theo cụ thể.
- Mã yêu cầu (FR-*, SEC-*, NFR-*) phải xuất hiện trong tên test hoặc comment, và được ghi vào `docs/TRACEABILITY.md`.
- Code, comment, commit và tài liệu kỹ thuật trong code bằng tiếng Anh. Chuỗi giao diện chỉ nằm trong resx, có đủ vi-VN và en-US (`.agents/rules/i18n.md`).
- Phiên bản package chỉ khai báo trong `Directory.Packages.props`.
- Đọc tài liệu chính thức hiện hành trước khi viết code gọi Google Calendar API, Google OAuth hoặc Gemini API. Ghi nguồn và ngày đọc vào ADR.
- Line ending CRLF và UTF-8 (`.editorconfig`). Hằng số đặt tên PascalCase; field private đặt tên `_camelCase`.

## 7. KHÔNG được làm

- Commit secret, API key, OAuth client secret, chứng chỉ (`*.pfx`) hay token. Không hard-code key, secret, OAuth client ID hay model ID (spec §5.9).
- Đổi AUMID của toast sau khi đã chốt (FR-NOTIFY-10, ADR-0014).
- Đổi schema DB mà không có migration mới kèm test nâng cấp (spec §6). Không sửa migration đã phát hành.
- Ghi dữ liệu chat vào outbox hoặc gửi lên data lake khi người dùng chưa bật toggle opt-in (FR-DATA-1, FR-DATA-2, FR-CLOUD-2).
- Xoá, skip hoặc làm yếu test đang fail để cho pass. Hãy sửa code, hoặc ghi lý do vào `docs/STATUS.md` và để test fail.
- Dùng `ServerCertificateValidationCallback` hoặc bất kỳ cách nào bỏ qua kiểm tra chứng chỉ.
- Ghi request hoặc response thô của Gemini ở mức log mặc định. Ghi key, token, header Authorization hay tiêu đề sự kiện vào log.
- Đặt secret trong query string (Gemini dùng header `x-goog-api-key`).
- Thêm telemetry, analytics hay tự động gửi crash report.
- Tắt analyzer hoặc warning bằng `#pragma` mà không có comment giải thích.
- Dùng `async void` ngoài event handler; block UI thread bằng `.Result` hoặc `.Wait()`.
- Cho model thực thi tool ghi hoặc xoá khi người dùng chưa xác nhận (FR-CHAT-2, SEC-PI-2).
- Thêm tham chiếu ngoài BCL vào Core, hoặc tham chiếu WPF/App vào Infrastructure.

## 8. Khi kết thúc task

Làm theo `.agents/workflows/finish-task.md` (`/finish-task`): build, test, format → cập nhật `docs/TRACEABILITY.md`, `CHANGELOG.md` nếu cần → đổi trạng thái task trong `docs/TASKS.md` → ghi nối tiếp `docs/PROGRESS_LOG.md` → ghi đè `docs/STATUS.md` (giữ mục của agent khác) → commit lên `task/<ID>` có trailer `Agent:` → Antigravity merge vào `phase/<N>`.
Xong task cuối của phase, Antigravity chuyển cổng `P<N>-REVIEW` sang `[r]` (log `READY`) rồi dừng. Chỉ Claude Code đặt cổng `[x]` và merge `phase/<N>` vào `main` sau khi review; tới lúc đó mới được sang phase kế tiếp (`.agents/rules/progress-sync.md` mục "Bàn giao và kết thúc"; mục này thắng nếu workflow mâu thuẫn).
Bắt đầu phiên mới: `/resume` (chỉ báo cáo) hoặc `/project-progress [task-id]` (báo cáo rồi làm tiếp). Cả hai lệnh dùng được trong Claude Code và Antigravity.
