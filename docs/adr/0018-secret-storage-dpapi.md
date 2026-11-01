# ADR-0018: Lưu bí mật bằng DPAPI (không dùng Credential Manager)

- **Status:** Accepted (2026-10-31). Spec §2, SEC-KEY-1, SEC-OAUTH-3 đã chốt.
- **Bối cảnh:** Gemini API key, refresh token, khoá SQLCipher và khoá outbox đều cần lưu an toàn theo user.
- **Quyết định:** Dùng `ISecretStore` (Core, `src/LunarCal.Core/Abstractions/`) hiện thực bằng `ProtectedData` scope `CurrentUser` có entropy riêng của app (`src/LunarCal.Infrastructure/Security/`). Không dùng Windows Credential Manager, vì spec không chọn nó. Muốn đổi thì phải ghi ADR mới và được chủ dự án duyệt.
- **Hệ quả:** DPAPI chỉ chống được user khác trên cùng máy, không chống được malware chạy cùng user. Giới hạn này đã ghi trong `docs/THREAT_MODEL.md`.
