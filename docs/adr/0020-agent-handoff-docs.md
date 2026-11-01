# ADR-0020: Hồ sơ bàn giao cho nhiều agent (AGENTS.md, STATUS, TASKS)

- **Status:** Accepted (2026-10-31).
- **Bối cảnh:** Dự án có thể được Claude Code hoặc Gemini (Google Antigravity) tiếp quản bất cứ lúc nào, kể cả giữa chừng một task.
- **Quyết định:** `AGENTS.md` ở gốc repo là nguồn hướng dẫn chung duy nhất; `CLAUDE.md` chỉ import nó (`@AGENTS.md`) và giữ phần riêng của Claude Code. Trạng thái nằm ở `docs/STATUS.md` (ghi đè mỗi phiên, không ghi nối tiếp), backlog ở `docs/TASKS.md`, quyết định ở `docs/adr/` (mỗi ADR một file; `docs/DECISIONS.md` chỉ còn là mục lục). Quy tắc theo chủ đề nằm ở `.agents/rules/`, quy trình ở `.agents/workflows/`.
- **Hệ quả:** Mỗi task phải cập nhật `docs/STATUS.md`. Không được chép nội dung spec vào các file này, chỉ trỏ tới số mục.
