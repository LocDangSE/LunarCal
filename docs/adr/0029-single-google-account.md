# ADR-0029: Một tài khoản Google tại một thời điểm; đổi tài khoản thì xoá dữ liệu cũ

- **Status:** Accepted (2026-11-07, quyết định của chủ dự án). Mục "Cách làm" do Claude Code đề xuất và đã được chủ dự án duyệt cùng ngày; giao cho P4-R2.
- **Bối cảnh:** Spec FR-AUTH-5 để "đa tài khoản Google" cho phase sau. Chủ dự án không có ý định dùng nhiều tài khoản cùng lúc. Kiểm tra code ngày 2026-11-07:
  - Cả ba đường đăng nhập (`FirstRunViewModel.SignInAsync`, `GoogleAccountViewModel.SignInAsync`, `MainWindowViewModel.ReauthAsync`) chỉ gọi `SaveAccountAsync`. Chọn một tài khoản Google khác thì app **âm thầm thêm tài khoản thứ hai**, giữ nguyên dữ liệu và refresh token của tài khoản cũ.
  - Đăng xuất (`GoogleSignOutService`, FR-AUTH-4) xoá `ReminderState`, `LunarRule`, `Event`, `CalendarInfo`, `Account` nhưng **không xoá `PendingMutation`** (bảng không có `account_id`, spec §6) và `AiCommand`. Thay đổi chưa đồng bộ của tài khoản cũ có thể bị phát lại lên tài khoản mới.
- **Quyết định:**
  1. App chỉ làm việc với **một** tài khoản Google. Không làm đa tài khoản (bỏ FR-AUTH-5). Mô hình dữ liệu vẫn giữ cột `account_id`, không đổi schema, không cần migration.
  2. Hai tài khoản được coi là một khi **Google user id** (`UserInfo.Id`) trùng, không so theo email.
  3. **Đăng nhập lại cùng tài khoản** (token hết hạn, ADR-0026): không xoá gì, giữ nguyên thay đổi chưa đồng bộ.
  4. **Đăng nhập tài khoản khác**: hiện hộp thoại xác nhận; nếu đồng ý thì xoá dữ liệu cũ rồi nạp dữ liệu của tài khoản mới (đồng bộ lần đầu). Nếu huỷ thì thu hồi và xoá token vừa cấp cho tài khoản mới, giữ nguyên trạng thái cũ.
  5. Dữ liệu bị xoá: refresh token của tài khoản cũ (có thu hồi), `Account`, `CalendarInfo` (kèm sync token), `Event`, `ReminderState`, `LunarRule`, `PendingMutation`, `AiCommand`.
  6. Dữ liệu được giữ: cài đặt (ngôn ngữ, giao diện, múi giờ, model Gemini, opt-in) và Gemini API key (thuộc về máy, không thuộc tài khoản Google).
  7. Hộp thoại xác nhận phải nêu rõ: tài khoản cũ sẽ bị gỡ khỏi máy này, **số thay đổi chưa đồng bộ** và **số lệnh AI đang chờ** sẽ mất, và **luật âm lịch chỉ lưu trên máy** cũng bị xoá.
- **Cách làm (đề xuất):**
  - Một dịch vụ dùng chung `AccountSessionService.ApplySignInAsync(userInfo)` trả về `FirstAccount`, `SameAccount` hoặc `DifferentAccount(pendingChanges, pendingAiCommands)`; ba đường đăng nhập đều gọi nó thay cho `SaveAccountAsync`.
  - Việc xoá dùng chung một hàm với đăng xuất, và bổ sung xoá `PendingMutation`, `AiCommand` (sửa luôn lỗi nêu ở Bối cảnh).
  - Test: cùng tài khoản không xoá; tài khoản khác sau khi xác nhận thì không còn dòng nào của tài khoản cũ ở mọi bảng kể cả `PendingMutation` và `AiCommand`; huỷ thì giữ nguyên và token mới bị xoá; refresh token cũ bị thu hồi.
- **Hệ quả (đã cập nhật 2026-11-07, trừ code):**
  - Spec: FR-AUTH-5 (§4.1), dòng FR-AUTH-5/6 ở bảng §15.2, dòng S20 ở bảng §15.4 (bỏ "đa tài khoản"). Đã sửa theo duyệt của chủ dự án.
  - `docs/TRACEABILITY.md` (dòng FR-AUTH-5), `.agents/rules/ui.md` (dòng S20).
  - Figma: S20 bỏ phần danh sách nhiều tài khoản (còn một thẻ tài khoản); thêm một hộp thoại "Đổi tài khoản Google?" cho bước 4.
- **Đã chốt:** luật âm lịch chỉ có trên máy bị xoá khi đổi tài khoản (khớp hành vi đăng xuất, FR-AUTH-4), và hộp thoại nêu rõ số luật sẽ mất. Muốn giữ thì phải thêm lựa chọn "giữ luật âm lịch" bằng ADR mới.
