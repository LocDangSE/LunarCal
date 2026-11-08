# ADR-0031: Lưu đề xuất của model trên `AiCommand` và chỉ thực thi qua dịch vụ xác nhận (P4-REVIEW)

- **Status:** Accepted (2026-11-08, Claude Code)
- **Bối cảnh:**
  - Review Phase 4 phát hiện chuỗi "gõ lệnh → model → thẻ xem trước → xác nhận → ghi lịch" (FR-CHAT-1, FR-CHAT-2, FR-CHAT-4) không chạy được trong app thật dù 753 test xanh: `IChatOrchestrator` và `IToolExecutor` không được đăng ký, không có hiện thực nào của `IToolExecutor`, đề xuất của model không được lưu ở đâu, hộp thoại xác nhận tự dựng tool call giả từ chữ người dùng gõ, và nút Xác nhận chỉ đặt trạng thái `completed` mà không tạo sự kiện.
  - Lệnh ở trạng thái `needs_confirmation` (FR-QUEUE-2) có thể chờ qua nhiều phiên làm việc, nên đề xuất phải sống ở đĩa, không ở bộ nhớ của orchestrator.
  - Spec §6 liệt kê cột `AiCommand` mà không có chỗ cho đề xuất. Đây là chỗ thiếu của spec (ghi ở `docs/STATUS.md`, mục Câu hỏi mở); không sửa spec, chọn phương án an toàn nhất.
- **Lựa chọn đã cân nhắc:**
  1. *Gọi lại model khi người dùng mở hộp thoại:* tốn quota, kết quả có thể khác với lần đầu, không dùng được khi offline. Loại.
  2. *Nhét JSON vào `last_error`:* lẫn nghĩa với lỗi. Loại.
  3. *Thêm cột `proposal_json` bằng migration V4:* một cột nullable, không đổi cột cũ. Chọn.
- **Quyết định:**
  1. Migration V4 thêm `AiCommand.proposal_json TEXT` (nullable). Có test nâng cấp V3 → V4 giữ nguyên lệnh đang chờ.
  2. Core thêm `AiCommandProposal(ToolCall?, ClarificationQuestion?, ClarificationAnswer?)`; `AiCommand.Proposal`; `IAiCommandStore.SetProposalAsync`.
  3. Worker lưu đề xuất ngay trước khi chuyển lệnh sang `needs_confirmation`. Với câu hỏi làm rõ (FR-CONFIRM-8), lưu câu hỏi; khi người dùng trả lời, lệnh về `pending` và lần chạy sau gửi kèm câu hỏi và câu trả lời.
  4. `IAiCommandConfirmationService` (Infrastructure) là **đường ghi duy nhất** cho lệnh trong hàng chờ: chỉ chạy khi lệnh đang `needs_confirmation`, kiểm tra lại bằng `ToolCallValidator`, gắn `requestId = lunarcal-ai-{id}` làm khoá idempotent (id sự kiện suy ra từ requestId nên xác nhận lặp không tạo bản trùng, FR-QUEUE-8).
  5. `CalendarToolExecutor` chỉ hiện thực `create_event` (phạm vi Phase 4, spec §13); các tool khác trả lỗi "chưa hỗ trợ" cho tới Phase 5.
  6. Nút "Xác nhận" ở ngăn kéo chỉ mở hộp thoại. Chỉ nút Xác nhận trong hộp thoại mới gọi dịch vụ xác nhận.
  7. Các lớp đọc đối số (`AiBusinessValidator`, `AiConfidenceEvaluator`, hộp thoại) đọc theo tên field của schema tool (`title`, `allDay`, `eventId`) qua `ToolArgumentReader`; tên cũ (`summary`, `all_day`, `event_id`) vẫn được chấp nhận.
  8. Cấu hình `LunarCal:Ai:QueueWorkerEnabled` (mặc định bật) cho phép tắt worker; UI smoke test dùng để không gọi key Gemini của người dùng.
- **Hệ quả:**
  - Hộp thoại xác nhận hiển thị đúng thứ model đề xuất; "Xem" với lệnh chưa có đề xuất mở hộp thoại chỉ đọc (Xác nhận bị khoá).
  - Phase 5 (update/delete/bulk/recurring) mở rộng `CalendarToolExecutor` và hộp thoại, không phải đổi lại luồng lưu và xác nhận.
