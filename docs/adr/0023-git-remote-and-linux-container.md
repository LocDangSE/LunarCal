# ADR-0023: Git, remote GitHub và container Linux cho Core

- **Status:** Accepted (2026-11-01).
- **Bối cảnh:** P0-01 cần khởi tạo git và chạy CI trên GitHub. Ngày 2026-11-01, chủ dự án chốt tên dự án là **LunarCal** (trả lời một phần spec §14 câu 1; biểu tượng và nhà phát hành vẫn mở) và chọn remote `https://github.com/LocDangSE/LunarCal.git`. Chủ dự án cũng yêu cầu cấu hình Docker. Spec không nhắc tới Docker. App là WPF nên chỉ chạy được trên Windows, còn Core (`net10.0`) phải độc lập với hệ điều hành (ADR-0002).
- **Quyết định:**
  1. Nhánh mặc định là `main`. CI chạy khi push lên `main` và khi có pull request (`.github/workflows/ci.yml`).
  2. `.gitattributes` cố định line ending: CRLF cho mã nguồn và tài liệu (theo `.editorconfig`), LF cho `Dockerfile`, `.dockerignore`, `*.sh`. File nhị phân (ảnh, font, `.parquet`, `.db`) được đánh dấu `binary`.
  3. `Dockerfile` chỉ build và test `LunarCal.Core` cùng `LunarCal.Core.Tests` trên image `mcr.microsoft.com/dotnet/sdk:10.0.403-noble`, ghim bằng digest (SEC-SC-1), restore ở `--locked-mode`. Job CI `core-linux-container` (ubuntu-latest) chạy `docker build --target test`. Docker không dùng để đóng gói hay phát hành app.
  4. `.dockerignore` theo kiểu allow-list: chỉ những file Dockerfile cần mới vào build context, để secret và file cấu hình cục bộ không lọt vào image (SEC-KEY-5).
  5. Thêm `.github/CODEOWNERS` (chủ dự án) và `.github/pull_request_template.md` (checklist build/test/format/secret/tài liệu).
- **Phương án đã loại:** Windows container cho toàn bộ solution: image lớn (vài GB), WPF không chạy được trong container, và runner `windows-latest` đã đáp ứng việc build. Dùng `docker-compose`: chưa có service nào cần chạy cùng (cloud ingest thuộc Phase 6b, nhà cung cấp chưa chốt, ADR-0015).
- **Hệ quả:** CI có thêm một job Linux, khoảng vài phút. Khi Dependabot đề xuất đổi digest, phải cập nhật cả `global.json` nếu SDK lên feature band mới. Nếu sau này Core lỡ dùng API chỉ có trên Windows, job Linux sẽ fail và chặn merge. Tên `LunarCal` đã chốt, nhưng câu §14.1 về biểu tượng và nhà phát hành vẫn mở (Q-001).
