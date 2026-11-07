# ADR-0017: Google OAuth 2.0 cho desktop app và quản lý client secret

- **Status:** Accepted (chốt tại task P3-05, ngày 2026-11-03).
- **Ngày đọc tài liệu Google:** 2026-11-03 (RFC 8252 OAuth 2.0 for Native Apps, RFC 7636 PKCE, Google Identity OAuth 2.0 for Desktop Apps).

## Bối cảnh

1. **Spec & yêu cầu bảo mật:**
   - Spec §4.1 (FR-AUTH-1, FR-AUTH-2, FR-AUTH-6): Đăng nhập OAuth 2.0 kiểu Desktop + PKCE, loopback `127.0.0.1` cổng ngẫu nhiên, kiểm tra tham số `state`. Mở trình duyệt mặc định của hệ thống, không nhúng WebView. Scope tối thiểu: `calendar.events`, `calendar.readonly`, `openid`, `email`, `profile`. Client ID lấy từ cấu hình build, cho phép ghi đè trong Settings > Nâng cao.
   - Spec §5.2 (SEC-OAUTH-1, SEC-OAUTH-2, SEC-OAUTH-3): PKCE S256, kiểm tra `state`, loopback chỉ bind `127.0.0.1`, đóng listener ngay sau callback, timeout 5 phút (300 giây). Token chỉ nằm trong DPAPI store, không ghi vào log.
   - Spec §11 & §14: Nếu Google yêu cầu client secret cho ứng dụng desktop thì phải ghi ADR; secret đó không thật sự bí mật với ứng dụng desktop công khai (RFC 8252 §8.5), nhưng tuyệt đối không commit vào repo công khai (SEC-KEY-5).

2. **Đặc tả chính thức của Google:**
   - Ứng dụng desktop (Installed/Desktop App) trong Google Cloud Console được khuyến nghị dùng luồng loopback IP address (`http://127.0.0.1:{port}/`). Google chỉ cho phép loopback IP (`127.0.0.1`) và không khuyến nghị `localhost` do nguy cơ phân giải sai tên miền trên các hệ điều hành khác nhau.
   - PKCE với phương thức `S256` là chuẩn bắt buộc cho client công khai để ngăn chặn authorization code interception attack.
   - Khi tạo OAuth client loại Desktop app trên Google Cloud Console, Google vẫn cấp một `client_secret` đi kèm `client_id`. Khi trao đổi mã (token exchange), nếu `client_secret` được thiết lập thì phải gửi kèm `code_verifier`.

## Quyết định

1. **Luồng xác thực loopback & PKCE:**
   - Luồng ủy quyền tuân thủ RFC 8252 và RFC 7636.
   - Tạo `PkceParameters`: sinh 32 bytes ngẫu nhiên an toàn (`RandomNumberGenerator`), mã hóa Base64URL không đệm làm `code_verifier` (độ dài 43 ký tự, nằm trong ngưỡng 43–128 ký tự unreserved của RFC 7636).
   - Tính toán `code_challenge = Base64Url(SHA256(code_verifier))` với `code_challenge_method = S256`.
   - Sinh tham số `state` ngẫu nhiên bảo mật 32 bytes (`OAuthState.Generate()`). Xác thực `state` trong callback bằng thuật toán so sánh chuỗi đẳng thời gian (`CryptographicOperations.FixedTimeEquals`) để chống tấn công CSRF. Bất kỳ callback nào có `state` không khớp hoặc thiếu đều bị từ chối ngay lập tức.
   - Loopback HTTP listener: Sử dụng `HttpListener` gắn chặt vào tiền tố `http://127.0.0.1:{port}/` với cổng `{port}` được cấp phát động ngẫu nhiên từ hệ điều hành. Tuyệt đối không bind vào `0.0.0.0` hay `localhost` (SEC-OAUTH-1).
   - Đóng listener: Đóng kết nối và giải phóng cổng ngay sau khi gửi trang HTML phản hồi cho người dùng, hoặc ngay khi hết timeout 5 phút (300 giây) (SEC-OAUTH-1).
   - Trình duyệt hệ thống: Mở bằng `Process.Start` với `UseShellExecute = true` qua abstraction `ISystemBrowser` (FR-AUTH-1). Không sử dụng bất kỳ WebView nhúng nào.

2. **Quản lý Client ID và Client Secret:**
   - Môi trường phát triển (Development): Lưu trữ qua .NET Secret Manager (`dotnet user-secrets`) trong project `LunarCal.App` với key `Google:ClientId` và `Google:ClientSecret` (SEC-KEY-5).
   - Môi trường phát hành (Production / CI): Client ID được nạp từ biến môi trường/build configuration hoặc file cấu hình không bí mật. Client Secret (nếu được cấp từ Google Console) nhận qua secret của CI khi build release.
   - Cho phép người dùng ghi đè Client ID qua giao diện Cài đặt > Nâng cao (FR-AUTH-6).
   - Hỗ trợ fallback cấu hình linh hoạt: `LunarCal:Google:OAuthClientId` -> `Google:ClientId` -> `LunarCal:Google:ClientId`.
   - Cấm hoàn toàn việc commit client secret, API key hay private token vào repo git (`.gitignore` và gitleaks chặn).

3. **Lưu trữ token:**
   - `refresh_token` nhận được từ Google được mã hóa an toàn vào `ISecretStore` (Windows DPAPI với entropy riêng của app, ADR-0018) dưới khóa `KnownSecretKeys.GoogleRefreshToken(accountId)` (SEC-OAUTH-3, FR-AUTH-3).
   - `access_token` chỉ được giữ trong bộ nhớ khi phiên làm việc đang chạy, không bao giờ ghi ra file, SQLite, registry hay log mặc định (SEC-LOG-1).

4. **Scope tối thiểu (Least Privilege):**
   - Chỉ yêu cầu các scope cần thiết:
     - `https://www.googleapis.com/auth/calendar.events` (đọc/ghi sự kiện)
     - `https://www.googleapis.com/auth/calendar.readonly` (đọc danh sách lịch và metadata)
     - `openid`, `https://www.googleapis.com/auth/userinfo.email`, `https://www.googleapis.com/auth/userinfo.profile` (nhận dạng tài khoản người dùng cho bảng `Account`).

## Hệ quả

- `src/LunarCal.App/appsettings.json` chỉ chứa placeholder `OAuthClientId: ""`.
- Core hoàn toàn độc lập với Google SDK và duy trì 100% BCL thông qua các abstraction `IGoogleOAuthClient`, `GoogleOAuthTokens`, `GoogleUserInfo`, `GoogleAuthResult`.
- Infrastructure triển khai trọn vẹn luồng loopback và token exchange bằng BCL thuần (`HttpListener`, `HttpClient`, `System.Security.Cryptography`), dễ dàng unit test và mock mà không phụ thuộc thư viện bên ngoài cồng kềnh.
