---
name: project-progress
description: Tiếp tục tiến độ LunarCal: nạp docs/STATUS.md và docs/TASKS.md, xác nhận build và test, rồi làm tiếp task đang dở (hoặc task ID truyền vào). Dùng khi muốn làm tiếp dự án.
argument-hint: "[task-id, ví dụ P2-01]"
---

# /project-progress

Quy trình đầy đủ nằm ở `.agents/workflows/project-progress.md`, là nguồn duy nhất (ADR-0021). Đọc toàn bộ file đó và làm đúng từng bước.
Skill này chỉ là lớp vỏ để Claude Code gọi bằng `/project-progress`; đừng chép nội dung quy trình vào đây.

Tham số người dùng truyền vào (có thể rỗng): $ARGUMENTS
