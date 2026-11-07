---
trigger: always_on
---

# Bảo mật (luôn áp dụng)

Nguồn: spec §5 (SEC-*), §4.5 (FR-BYOK), §4.1 (FR-AUTH), §5.9 (điều cấm khi sinh code), §5.10 (threat model). ADR: `docs/adr/0017`, `0018`, `0019`. Threat model: `docs/THREAT_MODEL.md`.

## BYOK: Gemini API key

- Key chỉ nằm trong `ISecretStore` (interface ở `src/LunarCal.Core/Abstractions/`, hiện thực DPAPI scope CurrentUser có entropy riêng của app ở `src/LunarCal.Infrastructure/Security/`). Xem SEC-KEY-1, FR-BYOK-3.
- Không lưu key trong `appsettings.json`, registry, biến môi trường, SQLite, log, crash dump hay data lake.
- Không dùng Windows Credential Manager: spec chọn DPAPI (ADR-0018). Muốn đổi phải có ADR mới và được chủ dự án duyệt.
- Gửi key qua header `x-goog-api-key`, không bao giờ đặt trong query string (SEC-KEY-2).
- Trong bộ nhớ: giải mã ngay trước khi dùng, không cache toàn cục, không đưa vào exception message, `ToString()` hay log (SEC-KEY-3).
- UI chỉ hiển thị dạng che, ví dụ `AIza••••xxxx` (FR-BYOK-3). Có nút xoá key và hướng dẫn thu hồi key (SEC-KEY-4, FR-BYOK-4).
- Key chỉ ở máy người dùng; không có server proxy gọi Gemini thay người dùng (spec §1 non-goals, §15.3).
- Biện pháp bổ sung ở Phase 4 (SEC-KEY-6..11, ADR-0016, spec §5.1): che key theo giá trị qua `SecretValueRegistry` (không chỉ theo mẫu `AIza`), `HttpClient` riêng cho Gemini với handler gắn key, host cố định và không theo redirect, bỏ qua key trong cấu hình, ô nhập `PasswordBox` xoá sau khi lưu và xoá clipboard, chỉ lưu key đã kiểm tra, luồng "nghi lộ key", test canary quét log, SQLite, `AppSetting` và outbox. Không bắt buộc key bắt đầu bằng `AIza`.
- Một tài khoản Google tại một thời điểm (FR-AUTH-5, ADR-0029): mọi đường đăng nhập đi qua `IAccountSessionService`; đừng gọi `IAccountStore.SaveAccountAsync` trực tiếp từ giao diện.

## OAuth và token Google

- OAuth desktop với PKCE S256, kiểm tra `state`, loopback chỉ bind `127.0.0.1` ở cổng ngẫu nhiên, đóng listener ngay sau callback, timeout 5 phút. Mở trình duyệt mặc định, không nhúng WebView (FR-AUTH-1, SEC-OAUTH-1).
- Scope tối thiểu (FR-AUTH-2, SEC-OAUTH-2). Không tự thêm scope.
- Refresh token lưu qua `ISecretStore` (DPAPI). Gặp `invalid_grant` thì yêu cầu đăng nhập lại (FR-AUTH-3). Đăng xuất thì revoke token và xoá token cùng cache (FR-AUTH-4, SEC-OAUTH-3).
- OAuth client ID lấy từ cấu hình build, không hard-code (FR-AUTH-6). Client secret (nếu Google yêu cầu): xem ADR-0017; dev dùng `dotnet user-secrets`, không commit.

## Dữ liệu cục bộ

- SQLite mã hoá SQLCipher. Khoá 256-bit ngẫu nhiên, bọc DPAPI (SEC-DATA-1).
- Thư mục `%LOCALAPPDATA%/LunarCal/` có ACL chỉ cho user hiện tại (SEC-DATA-2).
- Có nút xoá toàn bộ dữ liệu cục bộ (cache, token, key, outbox), có bước xác nhận (SEC-DATA-3).

## Mạng

- Chỉ HTTPS. Không bao giờ dùng `ServerCertificateValidationCallback` hay cách nào khác để tắt kiểm tra chứng chỉ (SEC-NET-1).
- Allow-list host: `LunarCal:Network:AllowedHosts` trong `src/LunarCal.App/appsettings.json`, cộng host ingestion và host cập nhật khi đã cấu hình.
- Timeout, retry có giới hạn, circuit breaker. Không retry request ghi không idempotent khi thiếu requestId hoặc ETag (SEC-NET-2).
- Không telemetry, analytics hay crash report tự gửi ra ngoài (SEC-NET-3, NFR-PRIV).

## Logging: KHÔNG được ghi

Serilog có redaction (SEC-LOG-1, ADR-0019). Không bao giờ ghi vào log:

- Gemini API key (mẫu `AIza…`), access token (`ya29…`), header `Authorization` hay `Bearer …`, `refresh_token`, client secret.
- Request hoặc response thô của Gemini ở mức mặc định. Nội dung chat chỉ ghi ở mức Debug, và Debug mặc định tắt (SEC-LOG-2).
- Tiêu đề, mô tả hay địa điểm của sự kiện (FR-NOTIFY-9). Email cũng bị che nếu được cấu hình.

LogRedactor phải có unit test. Có test quét log và lake sau một kịch bản đầy đủ để bảo đảm không có chuỗi giống key hoặc token (spec §9).

## AI và prompt injection

- Dữ liệu từ lịch hoặc nguồn ngoài đưa vào prompt phải được bọc trong khối có nhãn "dữ liệu không đáng tin" (SEC-PI-1, FR-CHAT-8).
- Model không có quyền thực thi trực tiếp. Mọi tool call phải qua validate schema, chính sách (giới hạn số lượng, khoảng thời gian ≤ 5 năm, không xoá hàng loạt) và xác nhận của người dùng (SEC-PI-2, FR-CHAT-2, FR-QUEUE-7).
- Không có tool đọc file, gọi URL hay gửi dữ liệu ra ngoài. Không render HTML từ model (SEC-PI-3).
- Bộ test prompt injection tự động (SEC-PI-4).

## Repo, CI, phát hành

- `.gitignore` đã chặn `*.user`, `secrets.*`, `*.pfx`, `.env`. gitleaks (pre-commit và CI) và CodeQL chưa được thêm (task P0-02, P0-03).
- Lock file cộng `RestoreLockedMode` trên CI, NuGet Audit bật ở mức `low` (`Directory.Build.props`), Dependabot (`.github/dependabot.yml`), action ghim theo commit SHA, `permissions: contents: read` (SEC-SC-1, 2).
- Ký Authenticode, auto-update qua HTTPS có ký hoặc checksum, không cần quyền admin, single instance bằng named mutex theo user (SEC-REL-1..4).
- Cloud: spec §5.11 (SEC-CLOUD-1..8); xem thêm `.agents/rules/data-lake.md`.
