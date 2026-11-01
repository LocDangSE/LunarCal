---
description: /resume - tiếp quản dự án LunarCal - đọc STATUS và TASKS, chạy build và test để xác nhận trạng thái thật, báo cáo, rồi đề xuất task tiếp theo.
---

# /resume: tiếp quản phiên làm việc

Mục đích: bắt đầu một phiên mới mà không cần biết phiên trước đã làm gì. Không sửa code trong quy trình này.

## Bước 1: Đọc hồ sơ

1. Đọc `AGENTS.md` ở gốc repo.
2. Đọc `docs/STATUS.md`. Ghi lại: phase hiện tại, task đang `[~]`, các file đang sửa dở, "Bước tiếp theo", và các câu hỏi mở.
3. Đọc `docs/TASKS.md`. Tìm task `[~]` (ghi lại agent đang giữ, theo phần `Đang làm:`), rồi tới task `[ ]` đầu tiên mà mọi task phụ thuộc của nó đã `[x]`.
4. Đọc 20 dòng cuối `docs/PROGRESS_LOG.md`. Liệt kê những gì agent khác (Claude Code hoặc Antigravity) đã làm kể từ dòng log cuối cùng của chính mình (`.agents/rules/progress-sync.md`).

## Bước 2: Xác nhận trạng thái thật (đừng tin STATUS.md một cách mù quáng)

Chạy tại gốc repo, trong PowerShell:

```powershell
git status --short
git log --oneline -10
dotnet restore LunarCal.slnx
dotnet build LunarCal.slnx
dotnet test --solution LunarCal.slnx
dotnet format LunarCal.slnx --verify-no-changes
```

- Nếu repo chưa phải git repository (`git status` báo lỗi), ghi nhận điều đó và bỏ qua hai lệnh git.
- So sánh với `docs/STATUS.md`: số test pass, fail, skip; các file đã thay đổi so với commit cuối. Nếu không khớp (ví dụ STATUS ghi "pass" nhưng thực tế có test fail, hoặc có file sửa dở mà STATUS không nhắc tới), coi kết quả lệnh là sự thật và ghi chênh lệch vào báo cáo.

## Bước 3: Báo cáo cho người dùng

Ngắn gọn, theo thứ tự:

1. Phase và task hiện tại (ID trong `docs/TASKS.md`), kèm agent đang giữ task `[~]`.
2. Việc agent khác đã làm kể từ phiên trước của mình (lấy từ `docs/PROGRESS_LOG.md`).
3. Build, test, format: pass hay fail; liệt kê test fail kèm lý do ngắn.
4. Phần dở dang từ phiên trước (theo STATUS và `git status`).
5. Câu hỏi mở đang chặn công việc (đặc biệt các mục thuộc spec §14, phải hỏi chủ dự án).
6. Chênh lệch giữa STATUS.md, PROGRESS_LOG.md và thực tế, nếu có.

## Bước 4: Đề xuất task tiếp theo

- Nếu có task `[~]` do chính mình giữ: đề xuất làm tiếp nó, kèm bước cụ thể lấy từ STATUS.md. Task `[~]` do agent khác giữ: không đề xuất tiếp quản, trừ khi log của task đó đã cũ hơn 24 giờ (`.agents/rules/progress-sync.md`).
- Nếu không: đề xuất task `[ ]` đầu tiên đã đủ phụ thuộc. Nêu ID, mã FR, các file sẽ đụng tới và tiêu chí hoàn thành (chép từ TASKS.md).
- Nếu task đó bị chặn (thiếu thiết kế Figma theo spec §15.4, hoặc chờ câu trả lời ở spec §14): nói rõ và đề xuất task khác không bị chặn.
- Chờ người dùng đồng ý rồi mới bắt đầu. Khi bắt đầu, nhận task theo mục "Nhận task" của `.agents/rules/progress-sync.md`: đọc lại TASKS.md, đổi sang `[~] · Đang làm: <agent>, <thời gian>`, ghi log `START`, cập nhật mục của mình trong `docs/STATUS.md`.
