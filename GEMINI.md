@AGENTS.md

# Riêng cho Gemini / Antigravity

- Hướng dẫn chung (lệnh, cấu trúc, quy tắc, điều cấm) nằm hết trong `AGENTS.md`. Đừng thêm nội dung chung vào file này; sửa ở `AGENTS.md` để các agent không bị lệch nhau (ADR-0020).
- Đầu phiên: gọi slash command `/resume` (hoặc làm theo `.agents/workflows/resume.md`).
- Cuối task, hoặc khi phiên sắp kết thúc: gọi slash command `/finish-task` (hoặc làm theo `.agents/workflows/finish-task.md`), luôn ghi đè `docs/STATUS.md` trước khi dừng.
- Khi gặp khoảng trống spec, mâu thuẫn hay điểm mơ hồ: gọi `/spec-gap` (làm theo `.agents/workflows/spec-gap.md`).
- Rule theo chủ đề: nằm tại `.agents/rules/` (`architecture.md`, `security.md`, `i18n.md`, `testing.md`, `data-lake.md`, `ui.md`). Mỗi rule đều có frontmatter `trigger` và `description` chuẩn Antigravity (ADR-0021).
- Commit và commit message: tiếng Anh, kèm mã yêu cầu (ví dụ: `feat(lunar): add ... (FR-LUNAR-4)`).
