---
trigger: model_decision
description: Quy trình review task của Claude Code (checklist, sửa bug kèm test tái hiện, ghi docs/reviews/<ID>.md). Đọc khi review hoặc khi task bị trả lại.
---

# Review task (Claude Code)

Workflow chung, trạng thái task và cách trả task: `.agents/rules/progress-sync.md`. ADR: `docs/adr/0025-executor-reviewer-roles.md`.

## Phạm vi

- Chỉ đọc diff của task: `git diff main...task/<ID>`, cộng các file mà diff tham chiếu tới (interface được hiện thực, resx, test liên quan) và mục spec ghi trong dòng task. Không đọc cả repo.
- Đối chiếu với "Xong khi" của task trong `docs/TASKS.md`, mã yêu cầu trên dòng task và mục tương ứng trong `docs/SPEC.md`.
- Task rủi ro thấp (UI nhỏ, rename, cấu hình): chỉ cần CI xanh và đọc lướt diff; ghi "review nhẹ" ở đầu file kết quả.

## Checklist

1. "Xong khi" đạt thật: chạy lại đúng lệnh hoặc test được nêu, không tin vào lời ghi trong log.
2. Test assert đúng hành vi: không `Skip`, không assert rỗng, không chỉ `NotBeNull`/`NotNull`; giá trị kỳ vọng cụ thể; mã yêu cầu có trong tên test.
3. Edge case nghiệp vụ: âm lịch (tháng nhuận, tháng thiếu 29 ngày, biên 1900–2100, Tết); i18n (vi-VN và en-US, đủ key ở cả hai resx); culture (định dạng ngày, ngày đầu tuần); timezone (đổi giờ, ngày qua nửa đêm, `DateTimeOffset`).
4. Không hard-code màu, kích thước hay chuỗi UI (dùng token theme và resx; `.agents/rules/ui.md`, `i18n.md`).
5. Không nới lỏng analyzer: không thêm `NoWarn`, `#pragma warning disable` không có lý do, không hạ `TreatWarningsAsErrors`, không đổi `.editorconfig` để né cảnh báo.
6. Bảo mật (`.agents/rules/security.md`): không log secret, token, header Authorization, tiêu đề sự kiện; không secret trong query string hay trong repo.
7. Kiến trúc: không vi phạm hướng phụ thuộc `App → Infrastructure → Core` (`.agents/rules/architecture.md`).
8. `TRACEABILITY.md` có mã yêu cầu mới; `STATUS.md` đã cập nhật.

## Sửa bug

- Mỗi bug sửa phải có test tái hiện: test fail trước khi sửa, pass sau khi sửa. Ghi tên test vào file kết quả.
- Mỗi bug một commit trên `task/<ID>`, message có mã yêu cầu, trailer `Agent: Claude Code`. Ghi log `FIX`.
- Sửa dài hơn khoảng 50 dòng hoặc phải đổi kiến trúc: không tự làm lại, trả task theo `progress-sync.md` ("Trả lại").
- Sau khi sửa: build, test, format pass trên branch, rồi mới đặt `[x]` và merge.

## Kết quả: `docs/reviews/<ID>.md`

Bốn mục, mỗi ý tối đa 2 dòng, kèm `file:line`. Mục trống ghi "Không có".

```markdown
# Review <ID>: <tên task ngắn>

Reviewer: Claude Code (<model>), <YYYY-MM-DD HH:mm +07:00> · Branch: task/<ID> · Kết luận: Duyệt | Trả lại | Review nhẹ

## Blocker
- (lý do trả task, nếu có) `path/file.cs:42`

## Đã sửa
- <bug> → <cách sửa>; test `<TênTest>` · commit <sha> · `path/file.cs:42`

## Nên sửa sau
- <ý> `path/file.cs:42`

## OK
- <điểm đã kiểm và đạt>
```
