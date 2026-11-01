# STATUS: ảnh chụp trạng thái hiện tại

> File này được **ghi đè** mỗi phiên, không ghi nối tiếp. Cập nhật sau mỗi task hoặc khi phiên sắp kết thúc (`AGENTS.md` §6).
> Mẫu câu hỏi mở: `.agents/workflows/spec-gap.md`.

**Cập nhật lần cuối:** 2026-11-01 10:45 +07:00, Claude Code (Opus 5.5). Phiên này làm P0-01: chốt tên LunarCal, khởi tạo git, thêm cấu hình git/Docker, push lên `https://github.com/LocDangSE/LunarCal.git` (ADR-0023).

## Phase và task

- **Phase hiện tại:** Phase 1 xong, đã xanh trên CI. Phase 0 còn P0-02..P0-04 (gitleaks, CodeQL, SourceLink). Phase 2 chưa bắt đầu.
- **Đang làm theo agent** (`.agents/rules/progress-sync.md`; mỗi agent chỉ viết lại mục của mình):
  - **Claude Code:** không giữ task nào (P0-01 vừa xong). Đã: `git init -b main`, `.gitattributes`, `.dockerignore`, `Dockerfile`, `.github/CODEOWNERS`, `.github/pull_request_template.md`, job CI `core-linux-container`, Dependabot cho Docker, ADR-0023, commit đầu tiên, push. P0-01 xong: CI run 41267053678 (commit 159781c) xanh cả `build-test` và `core-linux-container`. Không giữ task nào. File đang sửa dở: không có.
  - **Antigravity:** chưa có phiên nào được ghi lại.
- **Task tiếp theo đề xuất:**
  1. P0-02 (gitleaks), P0-03 (CodeQL), P0-04 (SourceLink).
  2. Trong lúc chờ thiết kế S06–S09, S13 (P2-00): các task Phase 2 không phụ thuộc Figma là P2-01 (i18n), P2-04 (setting), P2-05 (model sự kiện, fake store).

## Đã hoàn thành (đối chiếu spec §13)

| Phase | Trạng thái | Bằng chứng |
| --- | --- | --- |
| 0 | P0-00, P0-01 xong; CI xanh trên GitHub ngày 2026-11-01 (run 41267053678). Còn P0-02..P0-04 | `LunarCal.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, ADR-0001..0005 |
| 1 | Xong local: FR-LUNAR-1..3, coverage Lunar 100% | `src/LunarCal.Core/Lunar/`, `tests/LunarCal.Core.Tests/Lunar/`, `tests/LunarCal.Core.Tests/Fixtures/`, ADR-0006..0009, `docs/TRACEABILITY.md` |
| 2–8 | Chưa bắt đầu | `docs/TASKS.md` |

Còn thiếu so với spec §10 (CI): gitleaks và CodeQL (P0-02, P0-03); SourceLink (P0-04, SEC-SC-4); `release.yml` (P8-02).

## Build, test, format (chạy ngày 2026-11-01, Windows 11, .NET SDK 10.0.403)

| Lệnh | Kết quả |
| --- | --- |
| `dotnet restore LunarCal.slnx --locked-mode` | Pass |
| `dotnet build LunarCal.slnx -c Release` | Pass, 0 warning |
| `dotnet format LunarCal.slnx --verify-no-changes` | Pass |
| `dotnet test --solution LunarCal.slnx` | 71 test: 70 pass, 0 fail, 1 skip |
| Cổng coverage (`eng/Test-Coverage.ps1`) | Lunar 100%, Core 100% (ngưỡng 95% và 85%) |
| `dotnet run --project src/LunarCal.App` | Cửa sổ "LunarCal" mở được (shell còn trống; kiểm tra ngày 2026-10-31) |
| `docker build --target test .` (Linux, SDK 10.0.403-noble) | Pass: Core.Tests 69/69 |

- Test skip: `LunarCal.App.UiTests.SmokeTests.App_starts_and_shows_main_window`, lý do "UI shell is implemented in Phase 2". Bỏ skip ở P2-12.
- CI đầu tiên (commit f418386) đỏ ở job `core-linux-container`: `FR_LUNAR_2_A_month_grid_is_computed_in_under_5_ms` đo 7–9 ms (tái hiện bằng `docker run --cpus=2`). Nguyên nhân: `.Should().HaveCount(42)` nằm trong vùng bấm giờ, nên test đo cả chi phí lần gọi đầu của AwesomeAssertions (reflection, tìm tên biến). Engine thật chỉ mất 0,02–0,3 ms mỗi tháng. Đã chuyển assertion ra sau `stopwatch.Stop()` ở cả hai test hiệu năng, giữ nguyên ngưỡng 5 ms. Sau khi sửa: 9/9 lần pass với `--cpus` 0.5, 1 và 2; Windows 71 test: 70 pass, 0 fail, 1 skip; coverage Lunar/Core 100%.

## Quyết định đã chốt mà spec chưa ghi

Chi tiết nằm trong ADR (`docs/DECISIONS.md`):

- ADR-0003 AwesomeAssertions thay FluentAssertions (license). ADR-0004 xUnit v3 trên Microsoft.Testing.Platform. ADR-0005 `.slnx`.
- ADR-0006 lịch âm: phạm vi dương lịch 1900-01-01..2100-12-31, năm âm 1899..2100; ngoài phạm vi thì ném `ArgumentOutOfRangeException`; ngày âm không tồn tại thì `ToSolar` trả `null`; tháng nhuận dùng can chi của tháng thường; UTC+7 cho cả những năm trước 1968.
- ADR-0007 tiết khí được gán cho ngày (UTC+7) mà Mặt Trời đạt kinh độ; sai số tối đa khoảng 25 phút quanh nửa đêm.
- ADR-0008 tên can chi và tiết khí tiếng Việt nằm trong Core; chú giải tiếng Anh nằm trong resx (Phase 2).
- ADR-0009 bảng HKO 1901–2100 làm nguồn đối chiếu (trang Hồ Ngọc Đức trả HTTP 404).
- ADR-0018 dùng DPAPI, không dùng Credential Manager. ADR-0020 hồ sơ bàn giao. ADR-0021 workflow Antigravity gọi qua skill (`.agents/skills/`), vì `.agents/workflows/` không còn được index sau 19/10/2026.
- ADR-0022 đồng bộ tiến độ giữa Claude Code và Antigravity: người giữ task ghi trong TASKS.md, log chỉ ghi nối tiếp ở `docs/PROGRESS_LOG.md`, STATUS có mục riêng cho từng agent, commit có trailer `Agent:`.
- `.editorconfig`: hằng số đặt PascalCase, field private đặt `_camelCase`.
- ADR-0023: tên dự án **LunarCal** đã chốt (chủ dự án, 2026-11-01); nhánh `main`, remote `LocDangSE/LunarCal`; `Dockerfile` Linux chỉ build/test Core, image ghim digest; CI có job `core-linux-container`. Spec dòng 3 vẫn ghi "LunarCal là tên tạm". Agent không tự sửa spec, chủ dự án cập nhật khi cần.

## Câu hỏi mở

### Q-001: Câu hỏi bắt buộc cho chủ dự án (spec §14)

- **Mục spec:** §14
- **Loại:** Thiếu đặc tả (thuộc danh sách được phép hỏi)
- **Mô tả:** Chưa có câu trả lời cho: (1) biểu tượng, nhà phát hành (tên app đã chốt là LunarCal ngày 2026-11-01, ADR-0023); (2) OAuth Client ID và trạng thái consent screen; (3) chứng chỉ ký mã và kênh cập nhật; (4) mặc định có tự khởi động cùng Windows không; (5) nhà cung cấp, vùng và ngân sách cloud; (6) bên kiểm soát dữ liệu, liên hệ quyền riêng tư, độ tuổi tối thiểu; (7) xác nhận hai quyết định ở §15.3 (key Gemini chỉ ở máy người dùng; lịch âm mặc định lưu cục bộ); (8) bản tiếng Anh cho các chuỗi giao diện còn lại.
- **Ảnh hưởng:** P3-00, P6b-00, P7-00, P8-00; các task resx từ P2-01.
- **Đề xuất / tạm quyết:** Code hiện theo spec (key ở máy, lịch âm cục bộ). Không chặn Phase 2 trừ câu (8), với câu này tạm dùng bản dịch nháp.
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-002: Thiết kế Figma cho Phase 2 chưa có

- **Mục spec:** §13 (Điều chỉnh lộ trình v1.1), §15.4 (S06, S07, S08, S09, S13)
- **Loại:** Thiếu đặc tả
- **Mô tả:** Spec yêu cầu thiết kế xong màn hình của một phase trước khi bắt đầu phase đó. File Figma `4vwWjHGoSu9naj5ou86QYs` hiện chỉ có 5 frame 01–05 (đã kiểm tra ngày 2026-10-31) và không có variable hay design token.
- **Ảnh hưởng:** P2-03, P2-06..P2-11.
- **Đề xuất / tạm quyết:** Làm trước các task không cần thiết kế (P2-01, P2-02, P2-04, P2-05).
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-003: Bốn năm nhuận chưa có nguồn đối chiếu thứ hai

- **Mục spec:** §4.4, FR-LUNAR-3
- **Loại:** Mơ hồ (độ tin cậy của dữ liệu)
- **Mô tả:** Tháng nhuận của Việt Nam các năm 1917, 1922, 1938, 1987 khác HKO. Kết quả dựa trên thuật toán cộng lời giải thích lệch múi giờ, chưa có bảng âm lịch Việt Nam in sẵn để đối chiếu.
- **Ảnh hưởng:** Không chặn task nào.
- **Đề xuất / tạm quyết:** Giữ kết quả hiện tại (ADR-0009). Nếu có nguồn, thêm vào `tests/LunarCal.Core.Tests/Fixtures/`.
- **Trạng thái:** Tạm quyết
- **Ghi bởi:** Claude Code, 2026-10-31

### Q-004: Quyền phân phối lại dữ liệu HKO trong repo công khai

- **Mục spec:** FR-LUNAR-3
- **Loại:** Mơ hồ
- **Mô tả:** `tests/LunarCal.Core.Tests/Fixtures/hko-*.csv` lấy từ bảng của Hong Kong Observatory (cũng đăng trên DATA.GOV.HK). Điều khoản sử dụng cho việc phân phối lại là CHƯA XÁC MINH.
- **Ảnh hưởng:** P0-01, nếu repo để công khai.
- **Đề xuất / tạm quyết:** Chủ dự án đọc điều khoản trước khi công khai repo. Đã ghi nguồn trong `Fixtures/README.md`.
- **Trạng thái:** Mở
- **Ghi bởi:** Claude Code, 2026-10-31

## Nợ kỹ thuật

- CI chưa từng chạy trên GitHub (chưa có git repository hay remote). Lệnh trong `ci.yml` đã được chạy giả lập ở local.
- Thiếu gitleaks, CodeQL, SourceLink (P0-02..P0-04).
- Các package đã chốt ở spec §2 nhưng chưa thêm và chưa chọn phiên bản: Serilog, Google.Apis.*, Http.Resilience, SQLCipher, Parquet.Net, DuckDB, H.NotifyIcon.Wpf, Microsoft.Toolkit.Uwp.Notifications. Phải kiểm tra package còn được bảo trì trên .NET 10 trước khi thêm (ADR-0014).
- `docs/THREAT_MODEL.md` và `docs/DATA_LAKE.md` mới là khung; SECURITY.md, PRIVACY.md chưa có (P6b-07, P8-05).
- ADR-0001..0009 viết bằng tiếng Anh, ADR-0010 trở đi viết bằng tiếng Việt.
