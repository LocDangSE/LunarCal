# PROGRESS_LOG: nhật ký tiến độ giữa các agent

> **Chỉ ghi nối tiếp** ở cuối bảng, không sửa hay xoá dòng cũ. Quy tắc: `.agents/rules/progress-sync.md`.
> Agent: `Claude Code` hoặc `Antigravity`. Sự kiện: `START` · `PAUSE` · `DONE` · `BLOCKED` · `RELEASE` · `HANDOFF` · `NOTE`.
> Thời gian theo dạng `YYYY-MM-DD HH:mm +07:00`. Các dòng ghi ngày 2026-10-31 được điền lại từ `docs/STATUS.md`, nên không có giờ chính xác.

| Thời gian | Agent | Task | Sự kiện | Ghi chú (commit, bước tiếp theo) |
| --- | --- | --- | --- | --- |
| 2026-10-31 | Claude Code (Opus 5.5) | P0-00 | DONE | Scaffold solution, CI cơ bản, ADR-0001..0005. Chỉ chạy local, chưa có git. |
| 2026-10-31 | Claude Code (Opus 5.5) | P1-01 | DONE | Engine lịch âm, fixture HKO, coverage Lunar 100%. Chưa có git. |
| 2026-10-31 | Claude Code (Opus 5.5) | — | NOTE | Hồ sơ bàn giao: AGENTS.md, .agents/, STATUS, TASKS, ADR-0020, ADR-0021. |
| 2026-11-01 09:28 +07:00 | Claude Code (Opus 5.5) | — | NOTE | Thêm `/project-progress` và giao thức đồng bộ tiến độ (ADR-0022, `.agents/rules/progress-sync.md`, file log này). Thêm wrapper `.claude/skills/` cho mọi lệnh. Tiếp theo: P0-01 👤. |
| 2026-11-01 09:40 +07:00 | Claude Code (Opus 5.5) | P0-01 | START | Chủ dự án chốt tên LunarCal, remote https://github.com/LocDangSE/LunarCal.git. Thêm cấu hình git/Docker, git init, push. |
| 2026-11-01 10:05 +07:00 | Claude Code (Opus 5.5) | P0-01 | PAUSE | commit f418386 đã push lên `origin/main`. Repo private nên agent không đọc được kết quả Actions. Tiếp theo: chủ dự án xác nhận workflow `CI` xanh, sau đó đánh dấu P0-01 `[x]`. |
| 2026-11-01 10:30 +07:00 | Claude Code (Opus 5.5) | P0-01 | NOTE | CI f418386 đỏ ở job `core-linux-container`: test FR-LUNAR-2 đo cả assertion AwesomeAssertions (7–9 ms). Đã chuyển assertion ra ngoài stopwatch, giữ ngưỡng 5 ms. |
| 2026-11-01 10:45 +07:00 | Claude Code (Opus 5.5) | P0-01 | DONE | CI run 41267053678 xanh (build-test, core-linux-container), commit 159781c. Tiếp theo: P0-02 gitleaks. |
| 2026-11-01 10:04 +07:00 | Antigravity | P2-01 | START | Bắt đầu P2-01: hạ tầng resx vi-VN/en-US, localization service, ADR quy ước key, test FR-I18N-1, FR-I18N-3, FR-I18N-7 |
| 2026-11-01 10:20 +07:00 | Antigravity | P2-01 | DONE | Hạ tầng resx vi-VN/en-US, LocalizationService, ADR-0024, 15 test pass trong LunarCal.App.Tests. Tiếp theo: P2-02 hoặc P0-02 |
| 2026-11-01 10:43 +07:00 | Antigravity | P0-02 | START | Bắt đầu P0-02: cấu hình gitleaks vào CI và pre-commit hook (SEC-KEY-5, SEC-SC-3) |
| 2026-11-01 10:46 +07:00 | Antigravity | P0-02 | DONE | Cấu hình gitleaks trong ci.yml, .gitleaks.toml, .pre-commit-config.yaml, .githooks/pre-commit; kiểm tra chặn commit secret thành công. Tiếp theo: P0-03 |
| 2026-11-01 10:47 +07:00 | Antigravity | P0-03 | START | Bắt đầu P0-03: thêm CodeQL cho C# (.github/workflows/codeql.yml, ghim SHA, permissions tối thiểu) (SEC-SC-3) |
| 2026-11-01 10:48 +07:00 | Antigravity | P0-03 | DONE | Thêm CodeQL workflow cho C# (.github/workflows/codeql.yml, ghim SHA, build manual locked-mode). Tiếp theo: P0-04 |
| 2026-11-01 10:49 +07:00 | Antigravity | P0-04 | START | Bắt đầu P0-04: cấu hình SourceLink trong Directory.Build.props (PublishRepositoryUrl, EmbedUntrackedSources) (SEC-SC-4) |
| 2026-11-01 10:55 +07:00 | Antigravity | P0-04 | DONE | Cấu hình SourceLink trong Directory.Build.props & Directory.Packages.props (Microsoft.SourceLink.GitHub 10.0.403), lock files đã cập nhật, kiểm tra metadata repository trong PDB thành công (SEC-SC-4). Hoàn thành toàn bộ Phase 0. Tiếp theo: P2-02 |
| 2026-11-01 11:00 +07:00 | Antigravity | P2-02 | START | Bắt đầu P2-02: hiện thực pseudo-localization (kéo dài ~40%, có dấu, bảo toàn placeholder), cờ cấu hình và test FR_I18N_3_Pseudo_locale_transforms_every_key (FR-I18N-3) |
| 2026-11-01 11:08 +07:00 | Antigravity | P2-02 | DONE | Hiện thực PseudoLocalizer (kéo dài ~40%, có dấu, bảo toàn placeholder format), cờ cấu hình EnablePseudoLocale trong appsettings.json và ILocalizationService, 19 test pass trong LunarCal.App.Tests (FR-I18N-3). Tiếp theo: P2-04 |
| 2026-11-01 11:14 +07:00 | Antigravity | — | NOTE | Pull code mới từ origin/main: Dependabot PR #1 cập nhật github/codeql-action lên 4.39.1 (commit SHA 9b3254085494b55a0f7c4c2b3d2d52cc712ce370) cho init và analyze (SEC-SC-3). Tạm dừng trước P2-04 để theo dõi và đồng bộ tài liệu. |
