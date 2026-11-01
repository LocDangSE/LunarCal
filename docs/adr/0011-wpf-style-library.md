# ADR-0011: Thư viện style WPF (WPF UI/Fluent hay tự style)

- **Status:** Proposed. Chốt ở task P2-03 (`docs/TASKS.md`).
- **Bối cảnh:** Spec §2 cho chọn giữa "WPF UI (Fluent) hoặc tự style". Spec FR-UI-7 và FR-UI-8 yêu cầu theme sáng/tối/theo hệ thống, High Contrast, Per-Monitor V2 DPI. File Figma UI v1 chưa định nghĩa variable hay design token nào (đã kiểm tra ngày 2026-10-31).
- **Phương án:** (a) Thư viện WPF UI: có sẵn control Fluent, nhưng thêm một dependency và phải kiểm tra license, chế độ High Contrast và lock file. (b) Tự style bằng ResourceDictionary trong `src/LunarCal.App/Resources/`: kiểm soát hoàn toàn, nhưng tốn công hơn.
- **Hệ quả:** Phải chốt trước khi dựng shell UI ở Phase 2.
