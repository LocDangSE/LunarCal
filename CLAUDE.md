@AGENTS.md

# Riêng cho Claude Code

- Hướng dẫn chung (lệnh, cấu trúc, quy tắc, điều cấm) nằm hết trong `AGENTS.md`. Đừng thêm nội dung chung vào file này; sửa ở `AGENTS.md` để Gemini/Antigravity và Claude Code không bị lệch nhau (ADR-0020).
- **Vai của Claude Code: reviewer và người sửa bug** (ADR-0025). Workflow chung ở `.agents/rules/progress-sync.md`, cách review ở `.agents/rules/review.md`. Tóm tắt:
  - Lấy task `[r]` cũ nhất, đổi sang `[f]`, review `git diff main...task/<ID>`. Sửa bug kèm test tái hiện, commit lên chính branch đó, ghi `docs/reviews/<ID>.md`, rồi đặt `[x]` và merge vào `main`.
  - Sửa dài hơn khoảng 50 dòng hoặc phải đổi kiến trúc: không tự làm lại, trả task về Antigravity kèm lý do.
  - Không nhận task tính năng `[ ]`. Chỉ được làm task gắn 👤 hoặc task chủ dự án giao trực tiếp.
- Đầu phiên: làm theo `.agents/workflows/resume.md`. Muốn làm tiếp ngay task đang dở: gõ `/project-progress [task-id]` (skill ở `.claude/skills/project-progress/`, trỏ tới `.agents/workflows/project-progress.md`). Cuối task, hoặc khi usage sắp hết: làm theo `.agents/workflows/finish-task.md`, và luôn ghi đè `docs/STATUS.md` trước khi dừng.
- Luôn áp dụng: `.agents/rules/security.md` và `.agents/rules/progress-sync.md` (đồng bộ tiến độ với Antigravity, ADR-0022). Khi ghi `docs/TASKS.md`, `docs/PROGRESS_LOG.md` hoặc `docs/STATUS.md`, dùng tên agent `Claude Code (<model>)`.
- Slash command `/resume`, `/project-progress`, `/finish-task`, `/spec-gap` nằm ở `.claude/skills/`, chỉ là wrapper trỏ tới `.agents/workflows/`. Sửa quy trình ở workflow, không sửa ở wrapper.
- Rule theo chủ đề cần đọc khi làm task liên quan: `.agents/rules/review.md` (mọi lần review), `architecture.md`, `security.md`, `i18n.md`, `testing.md`, `data-lake.md`, `ui.md`. Phần frontmatter (`trigger`, `description`) là dành cho Antigravity; Claude Code chỉ cần đọc nội dung.
- Commit và PR theo quy ước attribution của Claude Code trong phiên làm việc.
