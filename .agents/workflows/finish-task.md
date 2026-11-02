---
description: /finish-task - kết thúc một task LunarCal - chạy build, test, format; đánh dấu task trong TASKS.md; cập nhật STATUS.md và TRACEABILITY; commit có mã FR.
---

# /finish-task: kết thúc task

Dùng khi task trong `docs/TASKS.md` đã xong, hoặc khi phải dừng giữa chừng (hết thời gian, sắp hết usage). Trường hợp dừng giữa chừng: làm bước 1, 2, 3b và 5, giữ task ở `[~]`, bỏ qua commit nếu build hoặc test đang fail.
Đồng bộ với agent khác theo `.agents/rules/progress-sync.md`. Thứ tự bắt buộc: TASKS → PROGRESS_LOG → STATUS → commit.

## Bước 1: Kiểm tra chất lượng

Chạy tại gốc repo, trong PowerShell:

```powershell
dotnet restore LunarCal.slnx
dotnet build LunarCal.slnx -c Release
dotnet format LunarCal.slnx --verify-no-changes
dotnet test --solution LunarCal.slnx -c Release --no-build --coverage --coverage-output-format cobertura --results-directory TestResults
pwsh -NoProfile -Command "./eng/Test-Coverage.ps1 -Reports 'TestResults/*.cobertura.xml' -Gate 'src/LunarCal.Core/Lunar=95','src/LunarCal.Core=85'; exit `$LASTEXITCODE"
```

- `dotnet format` báo lỗi: chạy `dotnet format LunarCal.slnx`, rồi chạy lại bước kiểm tra.
- Test fail: sửa code. Không xoá, skip hay làm yếu test. Nếu không sửa được trong phiên này: không commit, không đánh dấu `[x]`; ghi test fail và lý do vào `docs/STATUS.md`.
- Nếu task đổi package: kiểm tra các file `packages.lock.json` đã được cập nhật.
- Xoá thư mục `TestResults/` sau khi kiểm tra xong (đã nằm trong `.gitignore`).

## Bước 2: Tự rà soát

- `git diff`: không có secret, key, token, client ID hay model ID hard-code; không có chuỗi UI hard-code; không có `#pragma` thiếu comment giải thích; không có `async void` ngoài event handler; không có `.Result` hay `.Wait()` trên UI thread.
- Mỗi tiêu chí hoàn thành của task trong `docs/TASKS.md` đều đạt (test đã nêu tên phải tồn tại và pass).
- **Với task có sửa/cập nhật/dựng giao diện (UI):** Antigravity bắt buộc phải khởi động app (`dotnet run` / FlaUI) để đối chiếu trực tiếp với frame Figma nguồn; giao diện thực tế phải khớp 100% với Figma (bố cục, token, màu, font, bo góc, icon) thì mới được pass.

## Bước 3: Cập nhật tài liệu

1. `docs/TASKS.md` (đọc lại file ngay trước khi sửa): đổi trạng thái task thành `[x]` và thay `· Làm: ...` bằng `· Xong: <agent>, <thời gian>`; Antigravity merge `task/<ID>` vào `phase/<N>`. Nếu đây là task cuối của phase: chuyển cổng `P<N>-REVIEW` sang `[r]` và dừng, chờ Claude Code review (`.agents/rules/progress-sync.md`, ADR-0027). Nếu phát sinh việc mới, thêm task mới (ID tiếp theo trong phase, có đủ trường).
2. `docs/TRACEABILITY.md`: thêm hoặc cập nhật dòng cho mỗi FR-*, SEC-*, NFR-* mà task cover, ghi tên test đầy đủ.
3. `CHANGELOG.md`: thêm dòng vào `## [Unreleased]` nếu có thay đổi người dùng thấy được hoặc thay đổi đáng kể.
4. Quyết định kỹ thuật mới: tạo `docs/adr/NNNN-slug.md` và thêm vào mục lục `docs/DECISIONS.md`.
5. README của module, nếu API công khai thay đổi.

## Bước 3b: Ghi `docs/PROGRESS_LOG.md`

Ghi nối tiếp một dòng ở cuối bảng. Không sửa dòng cũ.

```text
| 2026-11-01 14:30 +07:00 | Claude Code (Opus 5.5) | P2-01 | DONE | commit abc1234. Tiếp theo: P2-02 |
```

- Sự kiện: `DONE` khi xong, `PAUSE` khi dừng giữa chừng (ghi bước tiếp theo cụ thể), `BLOCKED` khi bị chặn (task đổi sang `[!]`).
- Tên agent: `Claude Code` hoặc `Antigravity`, model ghi trong ngoặc nếu biết.

## Bước 4: Commit

- Nếu repo chưa là git repository: không tự `git init`; ghi vào STATUS.md và báo người dùng.
- Message bằng tiếng Anh, có mã yêu cầu, ví dụ:

```text
feat(sync): incremental sync with syncToken and 410 full resync (FR-SYNC-2)
```

- Chỉ stage file thuộc task. Không commit `TestResults/`, `bin/`, `obj/` hay file secret.
- Kết thúc một phase (task cuối của phase): tạo commit hoặc PR riêng cho phase đó (spec §0, §13).
- Thêm trailer `Agent: Claude Code` hoặc `Agent: Antigravity` vào cuối message, sau các trailer attribution của tool (nếu có).

## Bước 5: Ghi đè `docs/STATUS.md`

Đọc lại file ngay trước khi ghi. Viết lại theo cấu trúc sẵn có (không ghi nối tiếp thành log), nhưng **giữ nguyên mục của agent khác** trong "Đang làm theo agent":

- Phase hiện tại; mục của chính mình trong "Đang làm theo agent" (task đang giữ hoặc "không có"); task vừa xong; task tiếp theo đề xuất.
- Nếu đang dở: đã làm tới đâu, file nào đang sửa dở, bước tiếp theo cụ thể (lệnh hoặc file và hàm).
- Build, test, format: kết quả thật vừa chạy (số pass, fail, skip; tên test fail và lý do).
- Quyết định đã chốt mà spec chưa ghi; câu hỏi mở; nợ kỹ thuật.
- Dòng "Cập nhật lần cuối": ngày giờ (kèm múi giờ) và tool (Gemini/Antigravity hoặc Claude Code).
