# ADR-0017: OAuth client cho desktop và client secret

- **Status:** Proposed. Chốt ở task P3-05.
- **Bối cảnh:** Spec §11: nếu Google yêu cầu client secret cho ứng dụng desktop thì phải ghi ADR; secret đó không thật sự bí mật với app desktop, nhưng vẫn không được commit vào repo công khai. FR-AUTH-6 yêu cầu client ID lấy từ cấu hình build và cho phép ghi đè. Spec §14 câu 2 (Client ID, trạng thái consent screen) phải hỏi người dùng.
- **Hướng an toàn mặc định:** Dùng loopback `127.0.0.1` với PKCE S256 (SEC-OAUTH-1). Môi trường dev dùng `dotnet user-secrets`; bản phát hành nhận giá trị qua secret của CI (SEC-KEY-5). Đọc tài liệu OAuth hiện hành của Google trước khi chốt.
- **Hệ quả:** `src/LunarCal.App/appsettings.json` chỉ giữ `OAuthClientId` rỗng làm chỗ trống.
