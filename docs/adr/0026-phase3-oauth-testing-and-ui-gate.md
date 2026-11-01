# ADR-0026: Phase 3: consent screen Testing, làm task không có giao diện trước khi có thiết kế

- **Status:** Accepted (2026-11-01, chủ dự án duyệt).
- **Bối cảnh:**
  - Spec §14 câu 2 cần chủ dự án trả lời: OAuth Client ID và trạng thái consent screen. Chủ dự án đã tạo Client ID loại Desktop và lưu Client ID, client secret vào user-secrets của `src/LunarCal.App` (`UserSecretsId` trong csproj, commit f75d794).
  - Spec §13 (Điều chỉnh lộ trình v1.1): "Mỗi phase chỉ bắt đầu khi các màn hình thuộc phạm vi phase đó đã có thiết kế". Phase 3 cần S10, S14, S18, S20 (spec §15.4), hiện chưa có trong Figma. Trong 17 task Phase 3, chỉ P3-14 và P3-15 có giao diện.
- **Quyết định:**
  1. Consent screen để ở **Testing**. Ở chế độ này refresh token hết hạn sau 7 ngày. App xử lý bằng FR-AUTH-3 (`invalid_grant` thì yêu cầu đăng nhập lại) và FR-AUTH-6 (hướng dẫn rõ cho chế độ Testing), làm ở P3-06. Khi chuyển sang In production, code không phải đổi. Phương án khác: publish In production khi chưa verify (có màn hình cảnh báo "unverified app", giới hạn số người dùng, token không hết hạn sau 7 ngày). Hai mô tả này là CHƯA XÁC MINH với tài liệu Google hiện hành; P3-05 phải đọc tài liệu và ghi nguồn vào ADR-0017.
  2. Tách P3-00 thành P3-00a (Client ID và consent screen, đã xong) và P3-00b (4 frame Figma, 👤, còn chặn).
  3. **Ngoại lệ có chủ đích với spec §13:** các task Phase 3 không có giao diện (P3-01..P3-13, phần không có UI của P3-16) được làm trước khi có thiết kế. Task có giao diện (P3-14 cần S10; P3-15 cần S14, S18, S20) vẫn phải chờ P3-00b. Spec không bị sửa; chủ dự án cập nhật spec nếu muốn biến ngoại lệ này thành quy tắc chung.
  4. P3-00b phải vẽ đủ mọi mục "Nội dung bắt buộc" của S10, S14, S18, S20 ở spec §15.4, kể cả phần thuộc Phase 4 (S14: hướng dẫn lấy Gemini key kèm nút kiểm tra, hộp thoại đồng ý thu thập dữ liệu; S18: key sai, 429, bị chặn an toàn, không có calendar, không có sự kiện, skeleton; S20: đa tài khoản, model Gemini, vị trí dữ liệu, nhật ký), để không phải vẽ lại ở Phase 4.
- **Phương án đã loại:** Chờ đủ 4 frame rồi mới bắt đầu Phase 3. Cách này đúng chữ spec nhưng chặn khoảng 13 task không phụ thuộc giao diện.
- **Hệ quả:**
  - Cho tới khi P3-00b xong, agent không được làm P3-14, P3-15 và không dựng màn hình S10, S14, S18, S20.
  - Tên key hiện có trong user-secrets là `Google:ClientId` và `Google:ClientSecret`, còn `appsettings.json` dùng `LunarCal:Google:OAuthClientId`. P3-05 phải thống nhất tên key (ghi ở ADR-0017) và chủ dự án đặt lại user-secrets cho khớp.
  - `Host.CreateApplicationBuilder` chỉ nạp user-secrets khi môi trường là `Development`, nên muốn chạy thử đăng nhập ở máy dev phải đặt `DOTNET_ENVIRONMENT=Development`. P3-05 kiểm tra lại điểm này.
