---
description: /spec-gap - ghi lại chỗ spec LunarCal thiếu, mâu thuẫn hoặc mơ hồ thành một câu hỏi mở theo mẫu thống nhất trong docs/STATUS.md; không tự sửa spec.
---

# /spec-gap: ghi câu hỏi mở về spec

Dùng khi spec (`docs/SPEC.md`) thiếu đặc tả, mâu thuẫn với chính nó, mâu thuẫn với Figma, hoặc mơ hồ đến mức không làm tiếp được một cách an toàn.

## Quy tắc

- **Không sửa `docs/SPEC.md`** khi chưa được chủ dự án duyệt. Chỉ ghi câu hỏi.
- Kiểm tra trước, theo thứ tự: spec §15.3 (các mâu thuẫn Figma và spec đã có quyết định), các ADR trong `docs/adr/`, mục "Câu hỏi mở" và "Quyết định đã chốt" trong `docs/STATUS.md`. Nếu đã có câu trả lời thì dùng nó, không ghi trùng.
- Chỉ những mục ở spec §14 mới hỏi thẳng chủ dự án ngay. Các điểm khác: chọn phương án an toàn nhất, ghi ADR nếu đó là quyết định kỹ thuật, ghi câu hỏi ở trạng thái "Tạm quyết" và làm tiếp.
- Phương án an toàn nhất là phương án ít quyền nhất, không gửi dữ liệu ra ngoài, cần người dùng xác nhận, và dễ đảo ngược.

## Các bước

1. Xác định mục spec liên quan (số mục và mã yêu cầu, ví dụ `§4.12, FR-NOTIFY-6`).
2. Mở `docs/STATUS.md`, mục `## Câu hỏi mở`. Lấy số thứ tự tiếp theo theo dạng `Q-NNN`.
3. Thêm một khối theo mẫu dưới đây.
4. Nếu đã chọn phương án tạm: tạo ADR `docs/adr/NNNN-slug.md` (Status: Proposed), thêm vào `docs/DECISIONS.md`, và ghi số ADR vào câu hỏi.
5. Nếu câu hỏi chặn task đang làm: đánh dấu task là bị chặn trong STATUS.md và chuyển sang task khác không bị chặn.
6. Báo người dùng câu hỏi bằng một hai câu, kèm phương án đề xuất.

## Mẫu

```markdown
### Q-NNN: <tiêu đề ngắn>

- **Mục spec:** §x.y, <mã yêu cầu>
- **Loại:** Thiếu đặc tả | Mâu thuẫn trong spec | Mâu thuẫn Figma và spec | Mơ hồ
- **Mô tả:** <một hai câu: spec nói gì, thiếu gì hoặc chỗ nào mâu thuẫn>
- **Ảnh hưởng:** <task ID trong docs/TASKS.md, file hoặc module bị chặn>
- **Phương án:** (a) ... (b) ...
- **Đề xuất / tạm quyết:** <phương án an toàn nhất và lý do>; ADR-NNNN nếu có
- **Trạng thái:** Mở | Tạm quyết | Đã trả lời (<ngày>, <người trả lời>)
- **Ghi bởi:** <tool>, <ngày>
```

Khi chủ dự án trả lời: đổi Trạng thái thành "Đã trả lời", ghi câu trả lời vào khối đó, cập nhật ADR liên quan (Proposed → Accepted), và chỉ sửa `docs/SPEC.md` nếu chủ dự án yêu cầu rõ ràng.
