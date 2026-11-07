# Architecture Decision Records: mục lục

Mỗi ADR là một file trong `docs/adr/` (ADR-0020). File này chỉ là mục lục, đừng viết nội dung ADR ở đây.
ADR mới: tạo `docs/adr/NNNN-short-slug.md` theo mẫu bối cảnh → quyết định → hệ quả, rồi thêm một dòng vào bảng dưới.
Trạng thái: Proposed / Accepted / Superseded.

| ADR | Tiêu đề | Trạng thái |
| --- | --- | --- |
| [0001](adr/0001-dotnet-10-sdk.md) | .NET 10 SDK pinned via global.json | Accepted |
| [0002](adr/0002-target-frameworks.md) | Core `net10.0`, các project khác `net10.0-windows10.0.19041.0` | Accepted |
| [0003](adr/0003-awesomeassertions.md) | AwesomeAssertions thay FluentAssertions | Accepted |
| [0004](adr/0004-xunit-v3-mtp.md) | xUnit v3 trên Microsoft.Testing.Platform | Accepted |
| [0005](adr/0005-slnx.md) | Solution `.slnx` | Accepted |
| [0006](adr/0006-lunar-engine-scope.md) | Phạm vi và ngữ nghĩa API của engine lịch âm | Accepted |
| [0007](adr/0007-solar-term-attribution.md) | Cách gán tiết khí cho ngày và độ chính xác | Accepted |
| [0008](adr/0008-lunar-names-in-core.md) | Tên Can Chi và tiết khí tiếng Việt nằm trong Core | Accepted |
| [0009](adr/0009-hko-lunar-reference.md) | Bảng HKO làm nguồn đối chiếu lịch âm | Accepted |
| [0010](adr/0010-wpf-mvvm-generic-host.md) | WPF + MVVM + Generic Host | Accepted |
| [0011](adr/0011-wpf-style-library.md) | Thư viện style WPF | Accepted |
| [0012](adr/0012-packaging-msix-or-velopack.md) | Đóng gói MSIX hay Velopack | Proposed |
| [0013](adr/0013-local-storage-sqlcipher.md) | Lưu trữ cục bộ SQLCipher | Accepted (migration: Proposed) |
| [0014](adr/0014-tray-and-toast-libraries.md) | Thư viện tray và toast | Accepted (AUMID: Proposed) |
| [0015](adr/0015-data-lake-outbox-and-cloud.md) | Data lake: outbox cục bộ và cloud | Accepted (nhà cung cấp cloud: Proposed) |
| [0016](adr/0016-gemini-model-and-endpoint.md) | Model, endpoint Gemini và bảo vệ API key | Accepted |
| [0017](adr/0017-oauth-desktop-client-secret.md) | OAuth desktop client secret | Accepted |
| [0018](adr/0018-secret-storage-dpapi.md) | Lưu bí mật bằng DPAPI | Accepted |
| [0019](adr/0019-logging-serilog.md) | Logging Serilog có redaction | Accepted |
| [0020](adr/0020-agent-handoff-docs.md) | Hồ sơ bàn giao cho nhiều agent | Accepted |
| [0021](adr/0021-antigravity-workflows-as-skills.md) | Workflow Antigravity gọi qua skill | Accepted |
| [0022](adr/0022-agent-progress-sync.md) | Đồng bộ tiến độ giữa Claude Code và Antigravity | Accepted |
| [0023](adr/0023-git-remote-and-linux-container.md) | Git, remote GitHub và container Linux cho Core | Accepted |
| [0024](adr/0024-localization-infrastructure-and-key-conventions.md) | Hạ tầng song ngữ (i18n) và quy ước đặt key | Accepted |
| [0025](adr/0025-executor-reviewer-roles.md) | Antigravity thực thi, Claude Code review và sửa bug | Accepted |
| [0026](adr/0026-phase3-oauth-testing-and-ui-gate.md) | Phase 3: consent screen Testing, làm task không có giao diện trước khi có thiết kế | Accepted |
| [0027](adr/0027-phase-level-review-gate.md) | Claude Code review theo phase, cổng review bắt buộc trước phase kế tiếp | Accepted |
| [0028](adr/0028-google-calendar-sync-gateway.md) | Google Calendar API v3 Sync Gateway và SyncEngine | Accepted |
