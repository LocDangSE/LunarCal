---
trigger: always_on
---

# Đồng bộ tiến độ giữa các agent (luôn áp dụng)

Claude Code và Antigravity (Gemini) cùng làm trên repo này và không trao đổi trực tiếp với nhau được. Mọi thông tin bàn giao phải nằm trong file. Agent nào không ghi file thì agent kia coi như việc đó chưa xảy ra. ADR: `docs/adr/0022-agent-progress-sync.md`.

## Nguồn sự thật về tiến độ

| File | Ghi thế nào | Dùng để |
| --- | --- | --- |
| `docs/TASKS.md` | Sửa đúng dòng task | Trạng thái và người đang giữ từng task |
| `docs/PROGRESS_LOG.md` | **Chỉ ghi nối tiếp** ở cuối bảng, không sửa hay xoá dòng cũ | Lịch sử: ai làm gì, lúc nào |
| `docs/STATUS.md` | Ghi đè, nhưng **giữ nguyên mục của agent khác** | Ảnh chụp trạng thái hiện tại |
| `git log` (khi đã có git) | Commit có trailer `Agent:` | Bằng chứng về code đã xong |

Tên agent chuẩn (dùng y nguyên, để tìm kiếm được): `Claude Code` và `Antigravity`. Có thể thêm model trong ngoặc, ví dụ `Claude Code (Opus 5.5)`. Thời gian ghi theo dạng `YYYY-MM-DD HH:mm +07:00`.

## Đầu phiên (trong `/resume` và `/project-progress`)

1. Đọc 20 dòng cuối `docs/PROGRESS_LOG.md`. Tìm mọi dòng của agent **khác** kể từ dòng cuối cùng của chính mình, rồi báo cho người dùng: "Kể từ lần trước, <agent> đã: ...".
2. Đối chiếu với `docs/TASKS.md` và `git log`. Nếu log ghi `DONE` mà task chưa `[x]` (hoặc ngược lại), coi đó là chênh lệch: báo người dùng và kiểm tra bằng build/test, đừng tự sửa cho khớp.

## Nhận task (claim)

1. **Đọc lại `docs/TASKS.md` ngay trước khi sửa.** Agent kia có thể vừa nhận task đó.
2. Task đã `[~]` và do agent khác giữ:
   - Dòng log cuối của task đó cách đây chưa tới 24 giờ: **không đụng vào**. Chọn task khác hoặc hỏi người dùng.
   - Cũ hơn 24 giờ: hỏi người dùng trước khi tiếp quản. Khi tiếp quản thì ghi log `HANDOFF`.
3. Nhận task: đổi thành `[~]`, thêm vào cuối dòng task `· Đang làm: <agent>, <thời gian>`. Sau đó ghi log `START`.
4. Hai agent được làm song song **chỉ khi** trường "File" của hai task không trùng nhau. Nếu trùng, task sau phải đợi.

## Trong khi làm

- Chỉ sửa file thuộc task mình giữ, cộng với các file tài liệu dùng chung (`TASKS.md`, `STATUS.md`, `PROGRESS_LOG.md`, `TRACEABILITY.md`, `CHANGELOG.md`, `DECISIONS.md`).
- Với file dùng chung: đọc lại ngay trước khi sửa, và chỉ sửa đúng phần của mình. Không ghi đè cả file từ bản đọc lúc đầu phiên.
- Không sửa, không đánh dấu, không ghi log thay cho task của agent khác.

## Khi dừng hoặc xong (trong `/finish-task`)

1. `docs/TASKS.md`:
   - Xong: đổi thành `[x]`, thay `· Đang làm: ...` bằng `· Xong: <agent>, <thời gian>`.
   - Dừng giữa chừng: giữ `[~]` để agent kia biết task còn chủ.
   - Bỏ dở hẳn: trả về `[ ]` và xoá phần `Đang làm`.
2. Ghi nối tiếp một dòng vào `docs/PROGRESS_LOG.md`: `DONE`, `PAUSE`, `BLOCKED` hoặc `RELEASE`, kèm commit (nếu có) và bước tiếp theo.
3. `docs/STATUS.md`: viết lại mục của chính mình trong "Đang làm theo agent", cùng các mục dùng chung (build/test, câu hỏi mở...). Giữ nguyên mục của agent khác.
4. Commit (nếu repo đã có git): message theo `AGENTS.md` §6, thêm trailer `Agent: <agent>`.

Thứ tự bắt buộc: TASKS → PROGRESS_LOG → STATUS → commit. Nếu phiên bị ngắt giữa chừng, agent sau vẫn thấy ít nhất trạng thái task.

## Loại sự kiện trong log

`START` nhận task · `PAUSE` dừng giữa chừng, vẫn giữ task · `DONE` xong · `BLOCKED` bị chặn (đổi task sang `[!]`) · `RELEASE` trả task về `[ ]` · `HANDOFF` tiếp quản task của agent khác, có người dùng đồng ý · `NOTE` thay đổi ngoài backlog (tài liệu, tooling, sửa nhỏ).
