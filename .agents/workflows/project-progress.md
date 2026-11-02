---
description: /project-progress - nạp hồ sơ tiến độ LunarCal (docs/STATUS.md, docs/TASKS.md), xác nhận trạng thái thật rồi làm tiếp task đang dở.
---

# /project-progress: tiếp tục tiến độ dự án

Mục đích: một lệnh duy nhất để nạp hồ sơ tiến độ và làm tiếp. Khác `/resume` ở chỗ: nếu có task đang `[~]`, gọi lệnh này được coi là người dùng đã đồng ý làm tiếp task đó.

Tham số (tuỳ chọn): một task ID trong `docs/TASKS.md`, ví dụ `P2-01`.

## Hồ sơ tiến độ

| File | Vai trò |
| --- | --- |
| `docs/STATUS.md` | Ảnh chụp trạng thái: phase, task đang làm, file sửa dở, bước tiếp theo, câu hỏi mở |
| `docs/TASKS.md` | Backlog theo phase, trạng thái `[ ]` `[~]` `[x]` `[!]`, cổng review phase `P<N>-REVIEW` (`[r]` `[f]`), agent đang giữ, phụ thuộc, tiêu chí "Xong khi" |
| `docs/PROGRESS_LOG.md` | Log chỉ ghi nối tiếp: agent nào làm gì, lúc nào (đồng bộ Claude Code và Antigravity) |
| `docs/TRACEABILITY.md` | Mã yêu cầu ↔ test |
| `CHANGELOG.md` | Thay đổi đã phát hành |

## Bước 1: Nạp hồ sơ và xác nhận trạng thái

Làm đúng Bước 1–3 của `.agents/workflows/resume.md`: đọc STATUS, TASKS và PROGRESS_LOG, chạy build/test/format, báo cáo ngắn. Báo cáo phải có mục "Agent khác đã làm gì từ lần trước".

## Bước 2: Chọn task

1. Có tham số task ID: dùng task đó. Kiểm tra mọi task phụ thuộc đã `[x]` và task không bị chặn (👤, thiếu thiết kế Figma theo spec §15.4, chờ spec §14). Nếu chưa đủ điều kiện: báo lý do, đề xuất task khác và dừng.
   Nếu task đó đang `[~]` do agent khác giữ: áp dụng mục "Nhận task" của `.agents/rules/progress-sync.md`.
2. Không có tham số, có task `[~]` **do chính mình giữ**: chọn task đó. Task `[~]` do agent khác giữ thì bỏ qua (xem rule đồng bộ).
3. Không có tham số, không có task `[~]`: đề xuất task theo Bước 4 của `.agents/workflows/resume.md` rồi **chờ người dùng đồng ý**.

## Bước 3: Làm tiếp

- Task mới bắt đầu: nhận task theo `.agents/rules/progress-sync.md`: đọc lại TASKS.md, đổi sang `[~] · Đang làm: <agent>, <thời gian>`, ghi log `START`, cập nhật mục của mình trong `docs/STATUS.md`.
- Task đang dở: bắt đầu từ "Bước tiếp theo" và "File đang sửa dở" trong `docs/STATUS.md`.
- Tuân thủ `AGENTS.md` §6, §7, `.agents/rules/progress-sync.md` và các rule liên quan trong `.agents/rules/`.
- Xong task, hoặc phiên sắp kết thúc: làm theo `.agents/workflows/finish-task.md`.
