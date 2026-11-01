# ADR-0019: Logging bằng Serilog có redaction

- **Status:** Accepted (2026-10-31). Spec §2, SEC-LOG-1, SEC-LOG-2 đã chốt.
- **Quyết định:** Serilog ghi file sink, xoay vòng, giữ tối đa 14 ngày, mức mặc định Information. Destructuring policy và enricher che các mẫu `AIza…`, `ya29…`, `Bearer …`, `refresh_token` (và email nếu cấu hình). Nội dung chat chỉ ghi ở mức Debug, mặc định tắt. Không có sink gửi ra ngoài (SEC-NET-3).
- **Hệ quả:** Phải có LogRedactor và test của nó trước khi bất kỳ code OAuth hay Gemini nào ghi log (task P3-01). Phiên bản package CHƯA XÁC MINH.
