# ADR-0016: Model và endpoint Gemini

- **Status:** Proposed. Chốt ở task P4-00 sau khi đọc tài liệu chính thức hiện hành.
- **Bối cảnh:** Spec §7 và FR-BYOK-5 cấm hard-code model id. `src/LunarCal.App/appsettings.json` hiện có `LunarCal:Gemini:BaseUrl = https://generativelanguage.googleapis.com/` và `DefaultModelId = ""`.
- **Quyết định dự kiến:** Gọi REST `generateContent` với function calling ở chế độ AUTO. Key gửi qua header `x-goog-api-key` (SEC-KEY-2). Model mặc định đọc từ appsettings, người dùng đổi được trong Settings, và chỉ cho chọn model hỗ trợ function calling.
- **Hệ quả:** Ghi rõ model id, phiên bản API và nguồn tài liệu (kèm ngày đọc) vào ADR này khi chốt.
