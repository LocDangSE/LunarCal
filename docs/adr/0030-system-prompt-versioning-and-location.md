# ADR-0030: Vị trí lưu trữ và cơ chế nạp System Prompt có phiên bản (P4-05)

- **Status:** Accepted (2026-11-08, Antigravity)
- **Bối cảnh:**
  - Theo spec §7, FR-CHAT-6, FR-QUEUE-4 và task P4-05: System prompt cho Gemini cần được lưu ở file resource có phiên bản (`Prompts/system.v1.md`), để khi sinh prompt và ghi vào Data Lake (`ContributionOutbox`) thì luôn mang trường `prompt_version` phục vụ huấn luyện và phân tích.
  - Cần quyết định vị trí đặt file prompt trong solution, cơ chế nhúng (embedded resource vs external file) và cách dịch vụ trong code nạp nội dung template.
  - Quy tắc phân tầng kiến trúc của LunarCal: `App → Infrastructure → Core`. Core là domain thuần túy (`net10.0`), chứa contract `IChatModelClient`, định nghĩa tool (`ToolDefinitions`), validator (`ToolCallValidator`) và `ChatOrchestrator`.
- **Lựa chọn đã cân nhắc:**
  1. *Đặt file trong App hoặc thư mục gốc ứng dụng:* Khi deploy self-contained hoặc đóng gói (MSIX, Velopack), việc đọc file từ đĩa có thể gặp vấn đề về đường dẫn tương đối, phân quyền hoặc file bị xoá nhầm. Hơn nữa, Core không thể tự truy cập nếu không thông qua dependency injection phức tạp.
  2. *Đặt file trong Core dưới dạng EmbeddedResource (`src/LunarCal.Core/Prompts/system.v1.md`):* File prompt được biên dịch nhúng trực tiếp vào assembly `LunarCal.Core.dll`. Bất kỳ tầng nào (Core, Infrastructure, App) đều có thể truy xuất template một cách an toàn, tin cậy, không phụ thuộc hệ điều hành hay I/O đĩa.
- **Quyết định:**
  1. **Vị trí lưu trữ:** File prompt được đặt tại `src/LunarCal.Core/Prompts/system.{version}.md` (bản đầu tiên là `system.v1.md`).
  2. **Cơ chế nhúng:** Cấu hình trong `LunarCal.Core.csproj`:
     ```xml
     <ItemGroup>
       <EmbeddedResource Include="Prompts\*.md" />
     </ItemGroup>
     ```
  3. **Hợp đồng & Service trong Core:**
     - Interface `ISystemPromptProvider` nằm trong `LunarCal.Core.Abstractions`:
       - `string DefaultVersion => "v1";`
       - `string GetRawTemplate(string version = "v1");`
       - `string RenderSystemPrompt(PromptContext context, string version = "v1");`
     - Domain record `PromptContext` chứa: `DateTimeOffset TypedAt`, `string TimeZoneId`, `string Locale`, `IReadOnlyList<string> AvailableCalendars`.
     - Tuyệt đối không chứa bất kỳ trường nào của sự kiện lịch (`Summary`, `Description`, `Location`, v.v.) trong `PromptContext` để đảm bảo tuân thủ cam kết quyền riêng tư (SEC-PRIV, spec §7).
  4. **Cơ chế render:** Thay thế các placeholder chuẩn: `{{TYPED_AT}}`, `{{TIME_ZONE}}`, `{{USER_LOCALE}}`, `{{AVAILABLE_CALENDARS}}`.
- **Hệ quả:**
  - Build/packaging đơn giản, không cần copy file thủ công khi phát hành app.
  - Unit test trong `LunarCal.Core.Tests` có thể kiểm tra việc nạp template, format và bảo mật dữ liệu một cách độc lập trên mọi môi trường (Windows, Linux CI container).
  - Khi cần nâng cấp prompt trong tương lai (ví dụ `system.v2.md`), chỉ cần thêm file mới vào thư mục `Prompts/` và khai báo version mới mà không làm phá vỡ logic cũ.
