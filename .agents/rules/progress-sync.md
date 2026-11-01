---
trigger: always_on
---

# Phân vai và đồng bộ tiến độ giữa các agent (luôn áp dụng)

Claude Code và Antigravity (Gemini) cùng làm trên repo này và không trao đổi trực tiếp với nhau được. Mọi thông tin bàn giao phải nằm trong file. Agent nào không ghi file thì agent kia coi như việc đó chưa xảy ra. ADR: `docs/adr/0022-agent-progress-sync.md`, `docs/adr/0025-executor-reviewer-roles.md`.

File này là nơi duy nhất mô tả workflow chung. `AGENTS.md` và `CLAUDE.md` chỉ trỏ tới đây. Nếu workflow trong `.agents/workflows/` mâu thuẫn với file này, làm theo file này.

## Phân vai (ADR-0025)

| | Antigravity: người thực thi | Claude Code: reviewer và người sửa bug |
| --- | --- | --- |
| Nhận task | Task `[ ]` trong `docs/TASKS.md` | Chỉ task `[r]` để review; task gắn 👤; task chủ dự án giao trực tiếp. **Không** nhận task tính năng |
| Viết | Code **và** test | Bản sửa bug kèm test tái hiện (`.agents/rules/review.md`) |
| Branch | Tạo `task/<ID>` từ `main`, commit ở đó | Commit bản sửa lên chính `task/<ID>`, rồi merge vào `main` |
| Trước khi bàn giao | build, test, format; cập nhật `TRACEABILITY.md`, `STATUS.md` | Như cột bên trái, cộng `docs/reviews/<ID>.md` |
| Kết thúc | Chuyển task sang `[r]`. **Không** đặt `[x]` | Đặt `[x]` và merge, hoặc trả task về Antigravity |

Trả task về khi lỗi là sai thiết kế hoặc thiếu hẳn phần việc: bản sửa dài hơn khoảng 50 dòng, hoặc phải đổi kiến trúc. Claude Code không tự làm lại mà ghi lý do rồi trả task (xem "Trả lại").

**Mỗi lúc chỉ một agent được sửa một branch.** Người đang giữ branch là người có tên ở cuối dòng task trong `TASKS.md` (`Làm:` khi `[~]`, `Review:` khi `[f]`). Task ở `[r]` thì chưa ai giữ branch.

## Nguồn sự thật về tiến độ

| File | Ghi thế nào | Dùng để |
| --- | --- | --- |
| `docs/TASKS.md` | Sửa đúng dòng task | Trạng thái, người đang giữ task và branch |
| `docs/PROGRESS_LOG.md` | **Chỉ ghi nối tiếp** ở cuối bảng, không sửa hay xoá dòng cũ | Lịch sử: ai làm gì, lúc nào |
| `docs/STATUS.md` | Ghi đè, nhưng **giữ nguyên mục của agent khác** | Ảnh chụp trạng thái hiện tại |
| `docs/reviews/<ID>.md` | Claude Code ghi khi review | Kết quả review từng task |
| `git log` | Commit có trailer `Agent:` | Bằng chứng về code đã xong |

Tên agent chuẩn (dùng y nguyên, để tìm kiếm được): `Claude Code` và `Antigravity`. Có thể thêm model trong ngoặc, ví dụ `Claude Code (Opus 5.5)`. Thời gian ghi theo dạng `YYYY-MM-DD HH:mm +07:00`.

## Trạng thái task

`[ ]` chưa làm · `[~]` Antigravity đang làm · `[r]` chờ review · `[f]` Claude Code đang review/sửa · `[x]` xong (chỉ Claude Code được đặt) · `[!]` bị chặn.

Đuôi dòng task (thay đuôi cũ khi đổi trạng thái, giữ `Trả lại` nếu có):

- `· Làm: Antigravity, <thời gian>` khi `[~]` và `[r]`.
- `· Review: Claude Code, <thời gian>, <số bug đã sửa>` khi `[f]` và `[x]`.
- `· Trả lại: <lý do ngắn>` khi task bị trả về `[~]`.

Task 👤 hoặc task chủ dự án giao trực tiếp cho Claude Code: đi thẳng `[ ]` → `[~]` → `[x]` với đuôi `· Làm: Claude Code, <thời gian>`, vẫn trên branch `task/<ID>`.

## Đầu phiên (trong `/resume` và `/project-progress`)

1. Đọc 20 dòng cuối `docs/PROGRESS_LOG.md`. Tìm mọi dòng của agent **khác** kể từ dòng cuối cùng của chính mình, rồi báo cho người dùng: "Kể từ lần trước, <agent> đã: ...".
2. Đối chiếu với `docs/TASKS.md` và `git log`/`git branch -a`. Nếu log và trạng thái task lệch nhau (ví dụ log ghi `READY` mà task chưa `[r]`, hoặc task `[r]` mà không có branch `task/<ID>`), báo người dùng và kiểm tra bằng build/test, đừng tự sửa cho khớp.
3. Chọn việc theo vai: Antigravity lấy task `[ ]` (hoặc task bị trả lại cho mình); Claude Code lấy task `[r]` cũ nhất.

## Nhận task (claim)

1. **Đọc lại `docs/TASKS.md` ngay trước khi sửa.** Agent kia có thể vừa nhận task đó.
2. Task đã `[~]` hoặc `[f]` và do agent khác giữ:
   - Dòng log cuối của task đó cách đây chưa tới 24 giờ: **không đụng vào**. Chọn task khác hoặc hỏi người dùng.
   - Cũ hơn 24 giờ: hỏi người dùng trước khi tiếp quản. Khi tiếp quản thì ghi log `HANDOFF`.
3. Antigravity nhận task: `[ ]` → `[~]`, đuôi `· Làm: Antigravity, <thời gian>`, tạo branch `task/<ID>` từ `main`, ghi log `START`.
4. Claude Code nhận review: `[r]` → `[f]`, đuôi `· Review: Claude Code, <thời gian>, 0`, checkout `task/<ID>`, ghi log `REVIEW`.
5. Hai task được làm song song **chỉ khi** trường "File" của chúng không trùng nhau. Nếu trùng, task sau phải đợi.

## Trong khi làm

- Chỉ sửa file thuộc task mình giữ, trên branch `task/<ID>` của task đó, cộng với các file tài liệu dùng chung (`TASKS.md`, `STATUS.md`, `PROGRESS_LOG.md`, `TRACEABILITY.md`, `CHANGELOG.md`, `DECISIONS.md`, `docs/reviews/`).
- Với file dùng chung: đọc lại ngay trước khi sửa, và chỉ sửa đúng phần của mình. Không ghi đè cả file từ bản đọc lúc đầu phiên.
- Không sửa, không đánh dấu, không ghi log thay cho task của agent khác. Không commit lên branch mà agent khác đang giữ.

## Bàn giao và kết thúc (trong `/finish-task`)

### Antigravity: xong phần thực thi

1. build, test, format pass; cập nhật `TRACEABILITY.md`, `CHANGELOG.md` nếu cần; commit lên `task/<ID>` (trailer `Agent: Antigravity`) và push branch.
2. `TASKS.md`: `[~]` → `[r]`, giữ đuôi `· Làm: Antigravity, <thời gian kết thúc>`.
3. Log `READY` kèm commit cuối của branch.
4. `STATUS.md`: viết lại mục Antigravity.

### Claude Code: review xong

1. Review theo `.agents/rules/review.md`, ghi `docs/reviews/<ID>.md`. Mỗi bug sửa là một commit trên `task/<ID>` (trailer `Agent: Claude Code`), kèm test tái hiện.
2. Duyệt: `[f]` → `[x]`, đuôi `· Review: Claude Code, <thời gian>, <số bug đã sửa>`. Merge `task/<ID>` vào `main` sau khi build, test, format pass trên kết quả merge. Log `DONE` kèm commit merge.
3. Trả lại: xem mục dưới.
4. `STATUS.md`: viết lại mục Claude Code.

### Trả lại

Claude Code đổi `[f]` → `[~]`, đuôi `· Làm: Antigravity, <thời gian cũ> · Trả lại: <lý do ngắn>`, ghi chi tiết vào mục Blocker của `docs/reviews/<ID>.md`, log `RETURN`. Bản sửa nhỏ đã commit trên branch thì giữ lại. Antigravity sửa xong thì xoá `· Trả lại`, chuyển lại `[r]` như bình thường.

### Dừng giữa chừng

Giữ nguyên `[~]` hoặc `[f]` để agent kia biết task còn chủ; log `PAUSE`. Bỏ dở hẳn: trả về trạng thái trước khi nhận (`[ ]` hoặc `[r]`), xoá đuôi của mình, log `RELEASE`.

Thứ tự bắt buộc: TASKS → PROGRESS_LOG → STATUS → commit. Nếu phiên bị ngắt giữa chừng, agent sau vẫn thấy ít nhất trạng thái task. Commit message theo `AGENTS.md` §6, thêm trailer `Agent: <agent>`.

## Loại sự kiện trong log

`START` nhận task · `READY` Antigravity xong, chuyển `[r]` · `REVIEW` Claude Code nhận review, chuyển `[f]` · `FIX` một bug đã sửa trong lúc review (ghi commit) · `RETURN` trả task về Antigravity · `DONE` task `[x]` và đã merge · `PAUSE` dừng giữa chừng, vẫn giữ task · `BLOCKED` bị chặn (đổi task sang `[!]`) · `RELEASE` bỏ task · `HANDOFF` tiếp quản task của agent khác, có người dùng đồng ý · `NOTE` thay đổi ngoài backlog (tài liệu, tooling, sửa nhỏ).
