---
name: finish-task
description: Kết thúc một task LunarCal: chạy build, test, format; đánh dấu task trong docs/TASKS.md; cập nhật docs/STATUS.md và TRACEABILITY; commit có mã FR. Dùng khi xong task hoặc khi phải dừng giữa chừng.
---

# /finish-task

Quy trình đầy đủ nằm ở `.agents/workflows/finish-task.md`, là nguồn duy nhất (ADR-0021). Đọc toàn bộ file đó và làm đúng từng bước.
Skill này chỉ là lớp vỏ để Claude Code gọi bằng `/finish-task`; đừng chép nội dung quy trình vào đây.

Tham số người dùng truyền vào (có thể rỗng): $ARGUMENTS
