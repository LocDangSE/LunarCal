---
trigger: model_decision
description: Quy trình review phase của Claude Code (checklist, sửa bug kèm test tái hiện, ghi docs/reviews/PHASE<N>.md). Đọc khi review hoặc khi cổng review bị trả lại.
---

# Review phase (Claude Code)

Workflow chung, trạng thái cổng `P<N>-REVIEW` và cách trả lại: `.agents/rules/progress-sync.md`. ADR: `docs/adr/0025-executor-reviewer-roles.md`, `docs/adr/0027-phase-level-review-gate.md`.

Claude Code review **một lần cho cả phase**, khi cổng `P<N>-REVIEW` ở `[r]`, không review từng task.

## Phạm vi

- Đọc diff của cả phase: `git diff main...phase/<N>`, cộng các file mà diff tham chiếu tới (interface được hiện thực, resx, test liên quan) và mục spec ghi trên các dòng task. Không đọc cả repo. Diff lớn: đi theo từng task (`git log --merges main..phase/<N>` cho biết commit merge của mỗi `task/<ID>`), đọc `git diff <merge>^1 <merge>`.
- Đối chiếu với "Xong khi" của **từng** task trong phase ở `docs/TASKS.md`, mã yêu cầu trên dòng task, mục tương ứng trong `docs/SPEC.md`, và DoD của phase ở tiêu đề mục phase.
- Review lại sau khi trả cổng: chỉ xem phần thay đổi kể từ lần review trước (`git diff <commit-review-trước>..phase/<N>`).
- Task rủi ro thấp (UI nhỏ, rename, cấu hình): chỉ cần CI xanh và đọc lướt diff; ghi "review nhẹ" bên cạnh task đó trong file kết quả.

## Checklist

1. "Xong khi" của từng task và DoD của phase đạt thật: chạy lại đúng lệnh hoặc test được nêu, không tin vào lời ghi trong log.
2. Test assert đúng hành vi: không `Skip`, không assert rỗng, không chỉ `NotBeNull`/`NotNull`; giá trị kỳ vọng cụ thể; mã yêu cầu có trong tên test.
3. Edge case nghiệp vụ: âm lịch (tháng nhuận, tháng thiếu 29 ngày, biên 1900–2100, Tết); i18n (vi-VN và en-US, đủ key ở cả hai resx); culture (định dạng ngày, ngày đầu tuần); timezone (đổi giờ, ngày qua nửa đêm, `DateTimeOffset`).
4. Không hard-code màu, kích thước hay chuỗi UI (dùng token theme và resx; `.agents/rules/ui.md`, `i18n.md`).
5. Không nới lỏng analyzer: không thêm `NoWarn`, `#pragma warning disable` không có lý do, không hạ `TreatWarningsAsErrors`, không đổi `.editorconfig` để né cảnh báo.
6. Bảo mật (`.agents/rules/security.md`): không log secret, token, header Authorization, tiêu đề sự kiện; không secret trong query string hay trong repo.
7. Kiến trúc: không vi phạm hướng phụ thuộc `App → Infrastructure → Core` (`.agents/rules/architecture.md`).
8. `TRACEABILITY.md` có mã yêu cầu mới; `STATUS.md` đã cập nhật.

## Sửa bug

- Mỗi bug sửa phải có test tái hiện: test fail trước khi sửa, pass sau khi sửa. Ghi tên test vào file kết quả.
- Mỗi bug một commit trên `phase/<N>`, message có mã yêu cầu và ID task gốc, trailer `Agent: Claude Code`. Ghi log `FIX`.
- Sửa dài hơn khoảng 50 dòng hoặc phải đổi kiến trúc: không tự làm lại, trả cổng theo `progress-sync.md` ("Trả lại"), kèm task sửa `P<N>-R*` nếu cần.
- Sau khi sửa: build, test, format, coverage gate pass trên `phase/<N>`, rồi mới đặt cổng `[x]` và merge vào `main`.

## Kết quả: `docs/reviews/PHASE<N>.md`

Bốn mục, mỗi ý tối đa 2 dòng, kèm ID task và `file:line`. Mục trống ghi "Không có". Review lại sau khi trả cổng: thêm mục `## Lần <k>` ở cuối file, không xoá nội dung cũ.

```markdown
# Review Phase <N>: <tên phase ngắn>

Reviewer: Claude Code (<model>), <YYYY-MM-DD HH:mm +07:00> · Branch: phase/<N> · Task: P<N>-01..P<N>-xx · Kết luận: Duyệt | Trả lại

## Blocker
- (lý do trả cổng, nếu có) P<N>-xx `path/file.cs:42`

## Đã sửa
- P<N>-xx: <bug> → <cách sửa>; test `<TênTest>` · commit <sha> · `path/file.cs:42`

## Nên sửa sau
- P<N>-xx: <ý> `path/file.cs:42`

## OK
- <điểm đã kiểm và đạt>
```
