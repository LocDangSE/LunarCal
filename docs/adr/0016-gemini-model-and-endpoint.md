# ADR-0016: Model, endpoint Gemini và bảo vệ API key

- **Status:** Accepted (2026-11-07, Claude Code, theo yêu cầu của chủ dự án trong P4-00). Model mặc định chỉ là giá trị cấu hình, đổi bằng một dòng trong `appsettings.json` mà không cần ADR mới.
- **Phạm vi do chủ dự án chốt:** người dùng tự nhập và lưu API key, dùng model để chat tạo lịch, và key phải được bảo vệ để không bị lộ ra ngoài. Không thêm tính năng nào ngoài phạm vi đó.
- **Bối cảnh:** Spec §7 và FR-BYOK-5 cấm hard-code model id; `appsettings.json` có `LunarCal:Gemini:DefaultModelId = ""`. ADR này chốt sau khi đọc tài liệu hiện hành.

## Nguồn đã đọc (2026-11-07)

Các URL `ai.google.dev/...` đều chuyển hướng (301) sang `aistudio.google.com/...`; trang không ghi ngày xuất bản. Đọc bằng WebFetch.

- Models (`/api-reference/models`), generateContent (`/api-reference/generate-content`), migrate-to-interactions (`/docs/migrate-to-interactions`).
- Function calling (`/docs/function-calling`, bản `generate-content=true` và bản Interactions), thought signatures (`/docs/thought-signatures`).
- Model `gemini-3.8-flash` (`/docs/models/gemini-3.8-flash`, "Latest update: September 2026"), danh sách model (`/docs/models`), rate limits, pricing, API key, troubleshooting, API errors.

Phần **chưa xác minh được từ trang gốc** (phải kiểm ở P4-02 bằng test hợp đồng và một lần chạy thật với key của chủ dự án, không suy đoán): chữ hoa/thường của `toolConfig.functionCallingConfig.mode` trong REST, JSON chính xác của `functionResponse`, tham số `pageSize`/`pageToken` của `models.list`, tiền tố định dạng của key mới, và việc bảng lỗi chung (`api-errors`, viết cho Interactions API) có áp dụng cho `generateContent` hay không.

## Quyết định

### 1. Endpoint và API

- Dùng REST `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` với function calling chế độ AUTO, đúng spec §7. Google ghi `generateContent` "remains fully supported" và khuyến nghị Interactions API (`POST /v1beta/interactions`) cho phát triển mới; không có ngày ngừng hỗ trợ nào được công bố.
- Chưa chuyển sang Interactions: spec đã chốt `generateContent`, và `IChatModelClient` (P4-01) cách ly nhà cung cấp, nên đổi sang Interactions sau này chỉ sửa trong `Infrastructure/Gemini`. Xem lại khi Google công bố ngày ngừng hỗ trợ `generateContent`.
- `DefaultModelId` mặc định = `gemini-3.8-flash` (bản stable, hỗ trợ function calling, có gói miễn phí). Giá trị nằm trong `appsettings.json`, không nằm trong code (FR-BYOK-5). Lựa chọn thay thế người dùng có thể chọn ở Settings: `gemini-3.6-flash`, `gemini-3.5-flash-lite`, `gemini-3.1-flash-lite` (rẻ hơn).
- Không dùng alias `gemini-flash-latest`: Google có thể đổi model phía sau chỉ báo trước 2 tuần, làm hành vi gọi hàm đổi mà người dùng không biết. Không dùng `gemini-2.5-*` (đã deprecated, chuyển hướng từ 2026-12-01).

### 2. Giới hạn và tham số gửi đi

- `gemini-3.8-flash`: input 1.048.576 token, output 65.536 token. Lệnh chat tạo lịch rất ngắn (không gửi nội dung sự kiện, spec §7), nên đặt trần riêng `LunarCal:Gemini:MaxOutputTokens` (mặc định 2048) và `RequestTimeoutSeconds` 60.
- **Không gửi** `temperature`, `topP`, `topK` (Gemini 3.6 trở lên bỏ qua và sẽ trả 400 ở bản sau) và **không gửi** mức thinking `minimal` (model này không hỗ trợ, trả lỗi).
- Function calling với Gemini 3.x: client **phải** trả lại `thoughtSignature` nguyên vẹn trong đúng `part` khi phát lại lượt có function call, nếu không API trả 400. Vì `Core` không được chứa type của Gemini, `ChatResponse` mang một trường mờ `ProviderState` (chuỗi) để `GeminiChatModelClient` phát lại các `part` gốc. Luồng chính (model trả tool call, người dùng xác nhận, app tự thi hành) không gửi kết quả ngược lại cho model, nên chỉ lượt "hỏi lại" (P4-11) cần phát lại.
- Cách liệt kê model và kiểm tra key: `GET /v1beta/models` với header `x-goog-api-key`. Mỗi model có `supportedGenerationMethods`, `inputTokenLimit`, `outputTokenLimit`, `thinking` nhưng **không có trường "hỗ trợ function calling"**. Quy tắc lọc ở P4-04: giữ model có `generateContent`, bỏ tên chứa `tts`, `live`, `image`, `embedding`, `omni`, `latest`, rồi khi người dùng chọn một model thì gọi thử một request nhỏ có khai báo hàm để xác nhận. Đọc `nextPageToken` nếu có phân trang.

### 3. Chi phí, quota, riêng tư (hiển thị cho người dùng, FR-BYOK-5)

- Có gói miễn phí; quota tính theo **project**, không theo key (RPD đặt lại lúc nửa đêm giờ Pacific). Con số RPM/RPD/TPM của gói miễn phí không có trong tài liệu đã đọc, nên UI không hứa con số nào.
- Giá trả phí `gemini-3.8-flash` (mỗi 1 triệu token): vào $0,75, ra $3,75 đến hết 2026-12-31; từ 2027-01-01 là $1,50 và $7,50. Ghi lưu ý này ở UI chọn model.
- **Gói miễn phí: Google dùng nội dung để cải thiện sản phẩm; gói trả phí thì không.** UI phải nói rõ điều này cạnh chỗ nhập key, kèm cam kết của app là chỉ gửi câu lệnh, tên lịch, locale và mốc thời gian, không gửi nội dung sự kiện.

### 4. Phân loại lỗi (FR-BYOK-2, FR-CHAT-7, S18)

Vì chưa xác nhận bảng lỗi cho `generateContent`, client phân loại theo **cả mã HTTP lẫn trường `status`/`reason` trong thân lỗi**, không chỉ theo mã HTTP:

| Điều kiện | Phân loại | Hành động |
| --- | --- | --- |
| 400 `API_KEY_INVALID`, 401, 403 thông thường | Key sai hoặc không có quyền | Mở Settings > AI, không retry |
| 403 có chữ "reported as leaked" | Key bị Google đánh dấu lộ | Báo rõ, đề nghị xoá key và tạo key mới, không retry |
| 403 `FREE_TIER_VERIFICATION_REQUIRED` hoặc dự án không đủ điều kiện gói miễn phí | Cần xác minh hoặc liên kết billing | Hướng dẫn, không retry |
| 400 `FAILED_PRECONDITION` | Chưa đủ điều kiện (billing hoặc khu vực) | Thông báo chung, không đoán nguyên nhân |
| 404 `model_not_found` | Model không còn tồn tại | Mời chọn model khác |
| 429 giới hạn theo phút | Hết lượt tạm thời | Backoff luỹ thừa có jitter, retry |
| 429 `quota_exceeded` (hạn mức ngày) | Hết quota ngày | Báo giờ đặt lại, không retry |
| 500, 503, 408, hết thời gian | Lỗi tạm thời | Backoff và retry (chỉ khi request idempotent, SEC-NET-2) |
| `promptFeedback.blockReason` hoặc `finishReason` SAFETY, PROHIBITED_CONTENT, BLOCKLIST... | Bị chặn an toàn | Báo, không retry |
| `finishReason` MALFORMED_FUNCTION_CALL, MISSING_THOUGHT_SIGNATURE | Lỗi định dạng phía client | Ghi lỗi (không có nội dung), không hiển thị chi tiết thô |

Không retry 400, 402, 403 (tài liệu troubleshooting).

### 5. Chính sách của Google về key (ảnh hưởng cách hướng dẫn người dùng)

- Gemini API **từ chối key tiêu chuẩn không bị hạn chế**; từ 2026-05-28 key mới tạo trong AI Studio là "auth key" (gắn service account, mặc định giới hạn cho Gemini API, có cơ chế thu hồi nhanh khi lộ). Hướng dẫn ở S14: tạo key tại AI Studio (`https://aistudio.google.com/apikey`); nếu dùng key cũ thì đặt "Restrict to Gemini API only" trong Cloud Console.
- Tài liệu **không nêu tiền tố định dạng** của key. Vì vậy app **không được** bắt buộc key bắt đầu bằng `AIza` (xem mục bảo mật bên dưới).

## Bảo mật API key (chủ dự án yêu cầu: chống lộ key)

Các lớp bảo vệ đã có từ trước và giữ nguyên: DPAPI `CurrentUser` kèm entropy riêng của app qua `ISecretStore` (ADR-0018), thư mục dữ liệu có ACL chỉ cho user hiện tại (SEC-DATA-2), key chỉ gửi qua header `x-goog-api-key` (SEC-KEY-2), chỉ HTTPS và allow-list host (SEC-NET-1), Serilog có `LogRedactor` (SEC-LOG-1), gitleaks trên pre-commit và CI (SEC-KEY-5), không telemetry (SEC-NET-3).

**Bổ sung ở Phase 4** (đã ghi vào "Xong khi" của P4-02 và P4-03):

1. **Che theo giá trị thật, không chỉ theo mẫu.** `LogRedactor` hiện bắt mẫu `AIza…`; key mới có thể không theo mẫu đó. Thêm `SecretValueRegistry`: khi key được lưu hoặc nạp, đăng ký chính chuỗi key (và dạng URL-encode) để mọi sink log, thông báo lỗi và chuỗi hiển thị thay nó bằng `[REDACTED]`; giữ regex `AIza` làm lớp dự phòng. Xoá key thì gỡ khỏi registry. Test dùng key giả **không** theo mẫu `AIza` để chứng minh.
2. **Một HttpClient riêng cho Gemini, key chỉ gắn lúc gửi.** Key được thêm bởi một `DelegatingHandler` ngay trước khi gửi, không nằm trong `DefaultRequestHeaders` và không lưu trong field của client. Handler từ chối gửi nếu host đích không phải `generativelanguage.googleapis.com`. `AllowAutoRedirect = false` trên client này để key không bị chuyển tiếp theo redirect sang host khác. Test: redirect sang host lạ thì không có header key.
3. **Không đọc key từ cấu hình.** Khi khởi động, nếu thấy `LunarCal:Gemini:ApiKey` (hay `Gemini:ApiKey`, `GEMINI_API_KEY`) trong appsettings, biến môi trường hoặc user-secrets thì ghi cảnh báo (không in giá trị) và **bỏ qua**; key chỉ đọc từ `ISecretStore`. Việc này chặn dev vô tình để key trong file cấu hình rồi commit.
4. **Ô nhập an toàn.** Dùng `PasswordBox`, không bind thuộc tính chuỗi sang ViewModel qua `INotifyPropertyChanged`; đọc một lần khi bấm Lưu rồi xoá ô ngay. Không có nút "hiện key". Sau khi lưu, UI chỉ thấy dạng che (`AIza••••xxxx`, hoặc 4 ký tự cuối nếu key không có tiền tố này).
5. **Dọn clipboard.** Người dùng dán key từ AI Studio nên key nằm trong clipboard và lịch sử clipboard của Windows. Sau khi lưu thành công, app xoá clipboard **chỉ khi** nội dung clipboard đúng bằng key vừa lưu, và hiện lưu ý "đã xoá key khỏi clipboard".
6. **Chỉ lưu key đã kiểm tra hoặc lưu có chủ đích.** Nút Kiểm tra gọi `GET /v1beta/models`; chỉ lưu khi thành công. Khi offline có thêm lựa chọn "Lưu mà không kiểm tra". Không lưu key sai.
7. **Phản ứng khi key bị lộ.** Gặp lỗi "reported as leaked" hoặc người dùng bấm "Tôi nghi key bị lộ": xoá key khỏi store và bộ nhớ ngay (FR-BYOK-4), tạm dừng các lệnh AI đang chờ trong hàng chờ (không xoá lệnh), hiện hướng dẫn thu hồi và tạo key mới trên AI Studio theo thứ tự Google khuyến nghị (tạo key mới, cập nhật app, xoá key cũ, xem lại mức dùng và hoá đơn).
8. **Giữ key ngắn nhất trong bộ nhớ** (SEC-KEY-3): giải mã khi cần, không cache lâu, xoá mảng `char[]`/`byte[]` sau khi gắn header (nỗ lực tốt nhất). Không đưa key vào `Exception.Message`, `ToString()`, tên cache hay dữ liệu hàng chờ AI (`AiCommand`).
9. **Test canary toàn hệ thống.** Một test đặt một key giả dài, chạy kịch bản đầy đủ (lưu, kiểm tra, gửi lệnh lỗi 401/429, đổi, xoá) rồi quét toàn bộ log, thông báo lỗi, bảng SQLite, `AppSetting` và outbox để bảo đảm chuỗi key giả không xuất hiện ở đâu (đúng yêu cầu quét ở spec §9).
10. **Hướng dẫn phía Google trong S14 và Settings > AI:** tạo key riêng cho LunarCal, giới hạn cho Gemini API, đặt cảnh báo ngân sách và hạn mức trong Cloud Console, thu hồi ngay khi nghi lộ.

### Giới hạn phải nói thật với người dùng

DPAPI `CurrentUser` bảo vệ khỏi user khác và khỏi người lấy file ổ cứng, **không** bảo vệ khỏi mã độc chạy cùng tài khoản Windows (đã ghi trong ADR-0018 và `docs/THREAT_MODEL.md`); entropy riêng của app nằm trong file chạy nên chỉ làm khó, không phải bí mật. Vì vậy lớp bảo vệ cuối cùng là phía Google: hạn chế key cho Gemini API, đặt ngân sách, thu hồi nhanh. Không có cách nào trong app đảm bảo key không bị lấy nếu máy đã nhiễm mã độc.

## Hệ quả

- P4-01..P4-13 được phép bắt đầu. P4-02 đặt `DefaultModelId = "gemini-3.8-flash"` và thêm `MaxOutputTokens` vào `appsettings.json` (P4-00 chỉ đổi tài liệu).
- P4-02 phải có test hợp đồng cho `toolConfig`, `functionResponse`, `thoughtSignature` và một lần chạy thủ công với key thật để chốt các mục "chưa xác minh" ở trên; kết quả ghi vào phần "Cập nhật" của ADR này.
- Nếu Google công bố ngày ngừng `generateContent`, mở ADR mới để chuyển sang Interactions API.
