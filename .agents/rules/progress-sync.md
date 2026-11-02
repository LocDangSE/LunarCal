---
trigger: always_on
---

# Phân vai và đồng bộ tiến độ giữa các agent (luôn áp dụng)

Claude Code và Antigravity (Gemini) cùng làm trên repo này và không trao đổi trực tiếp với nhau được. Mọi thông tin bàn giao phải nằm trong file. Agent nào không ghi file thì agent kia coi như việc đó chưa xảy ra. ADR: `docs/adr/0022-agent-progress-sync.md`, `docs/adr/0025-executor-reviewer-roles.md`, `docs/adr/0027-phase-level-review-gate.md`.

File này là nơi duy nhất mô tả workflow chung. `AGENTS.md` và `CLAUDE.md` chỉ trỏ tới đây. Nếu workflow trong `.agents/workflows/` mâu thuẫn với file này, làm theo file này.

## Review theo phase (ADR-0027)

- Claude Code **không** review từng task. Antigravity làm lần lượt mọi task của một phase (ví dụ P3-01 → P3-16) trên branch `phase/<N>`, rồi mới bàn giao **cả phase** cho Claude Code review.
- Mỗi phase (từ Phase 3) có một dòng cổng review `P<N>-REVIEW` ở cuối mục phase trong `docs/TASKS.md`. Phase 6a, 6b, 6c là ba phase riêng, mỗi phase một cổng.
- **Cổng bắt buộc:** chưa được bắt đầu bất kỳ task nào của phase kế tiếp khi `P<N>-REVIEW` của phase trước chưa `[x]`. Điều kiện này áp dụng kể cả khi "Phụ thuộc" của task không ghi cổng. Ngoại lệ duy nhất: task 👤 (thiết kế Figma, câu hỏi spec §14) của phase sau được làm sớm, vì không phải code.
- `main` chỉ nhận code của phase đã qua review: chỉ Claude Code merge `phase/<N>` vào `main`.

## Phân vai (ADR-0025, ADR-0027)

| | Antigravity: người thực thi | Claude Code: reviewer và người sửa bug |
| --- | --- | --- |
| Nhận việc | Task `[ ]` của phase hiện tại | Cổng `P<N>-REVIEW` ở `[r]`; task gắn 👤; task chủ dự án giao trực tiếp. **Không** nhận task tính năng |
| Viết | Code **và** test | Bản sửa bug kèm test tái hiện (`.agents/rules/review.md`) |
| Branch | `task/<ID>` tạo từ `phase/<N>`; xong task thì merge vào `phase/<N>` | Commit bản sửa lên `phase/<N>`, rồi merge `phase/<N>` vào `main` |
| Xong một task | build, test, format; cập nhật `TRACEABILITY.md`, `STATUS.md`; đặt task `[x]` | Không tham gia |
| Xong cả phase | Chuyển cổng `P<N>-REVIEW` sang `[r]`. **Không** đặt cổng `[x]` | Ghi `docs/reviews/PHASE<N>.md`; đặt cổng `[x]` và merge, hoặc trả cổng về Antigravity |

Trả lại khi lỗi là sai thiết kế hoặc thiếu hẳn phần việc: một bản sửa dài hơn khoảng 50 dòng, hoặc phải đổi kiến trúc. Claude Code không tự làm lại mà ghi lý do rồi trả cổng (xem "Trả lại").

**Mỗi lúc chỉ một agent được sửa một branch.** Antigravity giữ `phase/<N>` khi cổng đang `[ ]` hoặc `[~]`; Claude Code giữ khi cổng `[f]`. Cổng ở `[r]` thì chưa ai giữ. Khi cổng `[r]` hoặc `[f]`, Antigravity không commit thêm vào `phase/<N>`.

## Nguồn sự thật về tiến độ

| File | Ghi thế nào | Dùng để |
| --- | --- | --- |
| `docs/TASKS.md` | Sửa đúng dòng task hoặc dòng cổng | Trạng thái, người đang giữ task và branch |
| `docs/PROGRESS_LOG.md` | **Chỉ ghi nối tiếp** ở cuối bảng, không sửa hay xoá dòng cũ | Lịch sử: ai làm gì, lúc nào |
| `docs/STATUS.md` | Ghi đè, nhưng **giữ nguyên mục của agent khác** | Ảnh chụp trạng thái hiện tại |
| `docs/reviews/PHASE<N>.md` | Claude Code ghi khi review phase | Kết quả review từng phase |
| `git log` | Commit có trailer `Agent:` | Bằng chứng về code đã xong |

Tên agent chuẩn (dùng y nguyên, để tìm kiếm được): `Claude Code` và `Antigravity`. Có thể thêm model trong ngoặc, ví dụ `Claude Code (Opus 5.5)`. Thời gian ghi theo dạng `YYYY-MM-DD HH:mm +07:00`.

## Trạng thái

Task thường: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong (đã merge vào `phase/<N>`, **chưa** review) · `[!]` bị chặn.

Cổng `P<N>-REVIEW`: `[ ]` phase đang làm · `[r]` mọi task của phase đã `[x]`, chờ review · `[f]` Claude Code đang review/sửa · `[~]` bị trả lại, Antigravity đang sửa · `[x]` đã review và merge vào `main` (chỉ Claude Code được đặt).

Đuôi dòng (thay đuôi cũ khi đổi trạng thái, giữ `Trả lại` nếu có):

- Task: `· Làm: <agent>, <thời gian>` khi `[~]`; `· Xong: <agent>, <thời gian>` khi `[x]`.
- Cổng: `· Làm: Antigravity, <thời gian>` khi `[r]`; `· Review: Claude Code, <thời gian>, <số bug đã sửa>` khi `[f]` và `[x]`; `· Trả lại: <lý do ngắn>` khi `[~]`.

Task 👤 hoặc task chủ dự án giao trực tiếp cho Claude Code: đi thẳng `[ ]` → `[~]` → `[x]` với đuôi `· Làm: Claude Code, <thời gian>`, trên branch `task/<ID>`. Task chỉ đổi tài liệu, thiết kế hoặc tooling thì merge thẳng vào `main`; task đổi code của phase đang làm thì merge vào `phase/<N>`.

## Đầu phiên (trong `/resume` và `/project-progress`)

1. Đọc 20 dòng cuối `docs/PROGRESS_LOG.md`. Tìm mọi dòng của agent **khác** kể từ dòng cuối cùng của chính mình, rồi báo cho người dùng: "Kể từ lần trước, <agent> đã: ...".
2. Đối chiếu với `docs/TASKS.md` và `git log`/`git branch -a`. Nếu log và trạng thái lệch nhau (ví dụ log ghi `READY` mà cổng chưa `[r]`, task `[x]` mà chưa có trong `phase/<N>`, hoặc cổng `[r]` mà còn task chưa `[x]`), báo người dùng và kiểm tra bằng build/test, đừng tự sửa cho khớp.
3. Chọn việc theo vai: Antigravity lấy task `[ ]` đầu tiên của phase hiện tại (phase đầu tiên có cổng chưa `[x]`), hoặc sửa cổng bị trả lại; Claude Code lấy cổng `[r]` cũ nhất. Không có cổng `[r]` thì Claude Code không có việc review.

## Nhận task (claim)

1. **Đọc lại `docs/TASKS.md` ngay trước khi sửa.** Agent kia có thể vừa nhận task đó.
2. Kiểm tra cổng: cổng `P<N-1>-REVIEW` của phase trước phải `[x]`. Chưa `[x]` thì dừng, báo người dùng.
3. Task đã `[~]` hoặc cổng đã `[f]` và do agent khác giữ:
   - Dòng log cuối của task đó cách đây chưa tới 24 giờ: **không đụng vào**. Chọn task khác hoặc hỏi người dùng.
   - Cũ hơn 24 giờ: hỏi người dùng trước khi tiếp quản. Khi tiếp quản thì ghi log `HANDOFF`.
4. Antigravity nhận task: `[ ]` → `[~]`, đuôi `· Làm: Antigravity, <thời gian>`. Nếu chưa có `phase/<N>` thì tạo từ `main` và push. Tạo `task/<ID>` từ `phase/<N>`, ghi log `START`.
5. Claude Code nhận review: cổng `[r]` → `[f]`, đuôi `· Review: Claude Code, <thời gian>, 0`, checkout `phase/<N>`, ghi log `REVIEW`.
6. Hai task được làm song song **chỉ khi** trường "File" của chúng không trùng nhau. Nếu trùng, task sau phải đợi.

## Trong khi làm

- Chỉ sửa file thuộc task mình giữ, trên branch của task đó, cộng với các file tài liệu dùng chung (`TASKS.md`, `STATUS.md`, `PROGRESS_LOG.md`, `TRACEABILITY.md`, `CHANGELOG.md`, `DECISIONS.md`, `docs/reviews/`).
- Với file dùng chung: đọc lại ngay trước khi sửa, và chỉ sửa đúng phần của mình. Không ghi đè cả file từ bản đọc lúc đầu phiên.
- Không sửa, không đánh dấu, không ghi log thay cho task của agent khác. Không commit lên branch mà agent khác đang giữ.

## Bàn giao và kết thúc (trong `/finish-task`)

### Antigravity: xong một task

1. build, test, format pass; cập nhật `TRACEABILITY.md`, `CHANGELOG.md` nếu cần; commit lên `task/<ID>` (trailer `Agent: Antigravity`).
2. Merge `task/<ID>` vào `phase/<N>`, chạy lại build và test trên kết quả merge, push `phase/<N>`.
3. `TASKS.md`: `[~]` → `[x]`, đuôi `· Xong: Antigravity, <thời gian>`.
4. Log `DONE` kèm commit merge trên `phase/<N>`.
5. `STATUS.md`: viết lại mục Antigravity.

### Antigravity: xong cả phase

Khi mọi task của phase đã `[x]` (kể cả task "Kết thúc Phase N"):

1. Trên `phase/<N>`: build Release, format, test + coverage gate pass (lệnh ở `.agents/workflows/finish-task.md` bước 1). Push.
2. `TASKS.md`: cổng `P<N>-REVIEW` `[ ]` → `[r]`, đuôi `· Làm: Antigravity, <thời gian>`.
3. Log `READY` kèm commit cuối của `phase/<N>`.
4. `STATUS.md`: ghi rõ đang chờ Claude Code review Phase N; không bắt đầu task của phase kế tiếp.

### Claude Code: review xong phase

1. Review theo `.agents/rules/review.md`, ghi `docs/reviews/PHASE<N>.md`. Mỗi bug sửa là một commit trên `phase/<N>` (trailer `Agent: Claude Code`), kèm test tái hiện.
2. Duyệt: cổng `[f]` → `[x]`, đuôi `· Review: Claude Code, <thời gian>, <số bug đã sửa>`. Merge `phase/<N>` vào `main` sau khi build, test, format pass trên kết quả merge. Log `DONE` kèm commit merge. Từ lúc này Antigravity được bắt đầu phase kế tiếp.
3. Trả lại: xem mục dưới.
4. `STATUS.md`: viết lại mục Claude Code.

### Trả lại

Claude Code đổi cổng `[f]` → `[~]`, đuôi `· Làm: Antigravity, <thời gian cũ> · Trả lại: <lý do ngắn>`, ghi chi tiết vào mục Blocker của `docs/reviews/PHASE<N>.md`, log `RETURN`. Nếu việc phải làm lại lớn, thêm task sửa vào phase (dạng `P<N>-R1`, `P<N>-R2`, ...). Bản sửa nhỏ đã commit trên `phase/<N>` thì giữ lại. Antigravity sửa xong (mọi task `P<N>-R*` đã `[x]`) thì xoá `· Trả lại`, chuyển cổng lại `[r]` như bình thường. Lần review sau chỉ cần xem phần thay đổi kể từ lần trả lại.

### Dừng giữa chừng

Giữ nguyên `[~]` (hoặc cổng `[f]`) để agent kia biết còn chủ; log `PAUSE`. Bỏ dở hẳn: trả về trạng thái trước khi nhận (`[ ]` hoặc cổng `[r]`), xoá đuôi của mình, log `RELEASE`.

Thứ tự bắt buộc: TASKS → PROGRESS_LOG → STATUS → commit. Nếu phiên bị ngắt giữa chừng, agent sau vẫn thấy ít nhất trạng thái task. Commit message theo `AGENTS.md` §6, thêm trailer `Agent: <agent>`.

## Loại sự kiện trong log

`START` nhận task · `DONE` task `[x]` (Antigravity: đã merge vào `phase/<N>`; Claude Code: cổng `[x]` và đã merge vào `main`) · `READY` Antigravity xong cả phase, cổng `[r]` · `REVIEW` Claude Code nhận review phase, cổng `[f]` · `FIX` một bug đã sửa trong lúc review (ghi commit) · `RETURN` trả cổng về Antigravity · `PAUSE` dừng giữa chừng, vẫn giữ task · `BLOCKED` bị chặn (đổi task sang `[!]`) · `RELEASE` bỏ task · `HANDOFF` tiếp quản task của agent khác, có người dùng đồng ý · `NOTE` thay đổi ngoài backlog (tài liệu, tooling, sửa nhỏ).
