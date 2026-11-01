# ADR-0022: Đồng bộ tiến độ giữa Claude Code và Antigravity

- **Status:** Accepted (2026-11-01).
- **Bối cảnh:** Chủ dự án dùng song song Claude Code và Antigravity (Gemini) trên cùng một repo. Hai tool không chia sẻ bộ nhớ hay phiên làm việc. `docs/STATUS.md` bị ghi đè mỗi phiên, nên mất lịch sử về việc ai đã làm task nào, và một agent có thể xoá phần đang dở của agent kia. Repo hiện chưa có git (P0-01), nên chưa thể dựa vào `git log`. Ngoài ra, Claude Code chỉ nạp skill từ `.claude/skills/`, còn Antigravity nạp từ `.agents/skills/` (ADR-0021).
- **Quyết định:**
  1. Giao thức nằm trong `.agents/rules/progress-sync.md` với `trigger: always_on`. `AGENTS.md` §6 trỏ tới rule này, nên Claude Code cũng phải tuân theo.
  2. Có ba nguồn tiến độ: `docs/TASKS.md` (trạng thái, người đang giữ task), `docs/PROGRESS_LOG.md` (log chỉ ghi nối tiếp) và `docs/STATUS.md` (ảnh chụp có mục riêng cho từng agent). Khi đã có git, thêm trailer `Agent:` vào commit.
  3. Nhận task bằng `[~] · Đang làm: <agent>, <thời gian>`. Task do agent khác giữ, có log trong vòng 24 giờ, thì không được đụng vào. Tiếp quản task cũ hơn phải có người dùng đồng ý và ghi log `HANDOFF`.
  4. Mỗi lệnh (`/resume`, `/project-progress`, `/finish-task`, `/spec-gap`) có một workflow duy nhất ở `.agents/workflows/` và hai wrapper mỏng: `.agents/skills/<name>/SKILL.md` cho Antigravity, `.claude/skills/<name>/SKILL.md` cho Claude Code.
- **Phương án đã loại:** Dùng file khoá riêng (`.lock`): dễ bị bỏ quên khi phiên bị ngắt, và không chứa lịch sử. Gộp log vào STATUS.md: trái với quy tắc ghi đè của STATUS.
- **Hệ quả:** Mỗi lần kết thúc task tốn thêm một dòng log. Giao thức chỉ dựa vào việc agent tự tuân thủ, không có cơ chế ép buộc. Ngưỡng 24 giờ là tuỳ chọn, chọn theo hướng an toàn (tránh hai agent cùng sửa một task).
