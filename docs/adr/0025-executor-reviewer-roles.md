# ADR-0025: Antigravity thực thi, Claude Code review và sửa bug

- **Status:** Accepted (2026-11-01). Bổ sung cho ADR-0022. Phần review từng task đã được thay bằng review theo phase (ADR-0027, 2026-11-02).
- **Bối cảnh:** Trước đây hai agent cùng nhận task tính năng và tự đánh dấu `[x]`. Review Phase 2 (`docs/reviews/PHASE2.md`) cho thấy nhiều lỗi lọt qua: test assert yếu, edge case âm lịch và i18n bị thiếu, giá trị bị hard-code. Không có bước kiểm tra độc lập trước khi task được coi là xong.
- **Quyết định:**
  1. Antigravity là người thực thi: nhận task `[ ]`, viết code và test trên branch `task/<ID>`, chạy build/test/format, cập nhật TRACEABILITY/STATUS, rồi chuyển task sang `[r]`. Antigravity không đặt `[x]`.
  2. Claude Code là reviewer và người sửa bug: review `git diff main...task/<ID>` theo `.agents/rules/review.md`, sửa bug kèm test tái hiện trên chính branch đó, ghi `docs/reviews/<ID>.md`, rồi đặt `[x]` và merge. Claude Code không nhận task tính năng, chỉ làm task 👤 hoặc task chủ dự án giao trực tiếp.
  3. Lỗi là sai thiết kế hoặc thiếu hẳn phần việc (sửa dài hơn khoảng 50 dòng hoặc phải đổi kiến trúc): Claude Code trả task về Antigravity kèm lý do, không tự làm lại.
  4. Thêm trạng thái `[r]` (chờ review) và `[f]` (đang review/sửa); thêm sự kiện log `READY`, `REVIEW`, `FIX`, `RETURN`. Mỗi lúc chỉ một agent được sửa một branch; người giữ branch có tên ở cuối dòng task.
  5. Workflow chung chỉ mô tả ở `.agents/rules/progress-sync.md`; `AGENTS.md` và `CLAUDE.md` trỏ tới đó.
- **Phương án đã loại:** Review chéo hai chiều (mỗi agent review task của agent kia): khó giữ chuẩn review thống nhất, và vẫn có hai luồng thực thi chạm cùng file. Chỉ dựa vào CI: CI không bắt được test assert yếu hay edge case nghiệp vụ còn thiếu.
- **Hệ quả:** Mỗi task thêm một vòng review, nên chậm hơn nhưng có kiểm tra độc lập. `main` chỉ nhận code đã review. Các workflow trong `.agents/workflows/` (`finish-task`, `project-progress`, `resume`) vẫn mô tả mô hình cũ, cần cập nhật sau; trong lúc chờ, `progress-sync.md` thắng khi có mâu thuẫn. Ngưỡng 50 dòng là ước lượng, Claude Code tự đánh giá.
