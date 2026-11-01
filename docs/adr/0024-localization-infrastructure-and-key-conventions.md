# ADR-0024: Hạ tầng song ngữ (i18n) và quy ước đặt key

- **Status:** Accepted (2026-11-01).
- **Bối cảnh:** Task P2-01 yêu cầu triển khai hạ tầng song ngữ vi-VN và en-US (FR-I18N-1..7), hỗ trợ đổi ngôn ngữ trực tiếp khi đang chạy mà không cần khởi động lại ứng dụng, và tự động chọn ngôn ngữ theo hệ điều hành (tiếng Việt nếu OS là vi, ngược lại en-US; fallback vi-VN). Hai câu hỏi kỹ thuật được để mở trong `.agents/rules/i18n.md` cần được chốt: (1) vai trò của tệp `Strings.resx` trung lập cạnh các tệp `Strings.vi-VN.resx` và `Strings.en-US.resx`, và (2) quy ước đặt key trong resource.
- **Quyết định:**
  1. **Cấu trúc tệp tài nguyên**:
     - `Strings.resx`: Tệp tài nguyên gốc/trung lập (neutral resources), chứa chuỗi tiếng Việt tương ứng với thiết lập `<NeutralLanguage>vi-VN</NeutralLanguage>` trong `Directory.Build.props`. Tệp này được nhúng trực tiếp vào assembly chính (`LunarCal.dll`) làm fallback cho `ResourceManager`.
     - `Strings.vi-VN.resx` và `Strings.en-US.resx`: Được biên dịch thành các satellite assembly tương ứng (`vi-VN/LunarCal.resources.dll` và `en-US/LunarCal.resources.dll`).
     - Unit test `FR_I18N_3_Every_key_has_non_empty_vi_and_en` bảo đảm mọi key đều có mặt đầy đủ ở cả ba tệp với giá trị không rỗng.
  2. **Quy ước đặt tên key**:
     - Các key giao diện thông thường dùng phân cấp chữ thường nối bằng dấu chấm: `area.element.variant` (ví dụ `settings.language.title`, `confirm.privacy.on`, `consent.accept`, `view.month`, `can.giap`, `solarterm.xuan_phan`).
     - Riêng chuỗi toast thông báo Windows tuân thủ đặc tả spec §4.11 dùng tiền tố PascalCase: `Toast_<Loại>_<ChiTiết>` (ví dụ `Toast_Header_Reminder`, `Toast_Time_Remaining`).
     - Mọi chuỗi trong bảng spec §4.11 được sao chép chính xác từng ký tự (có unit test `FR_I18N_7_Required_spec_table_strings_match_exact_values`).
  3. **Cơ chế cập nhật trực tiếp khi đổi ngôn ngữ (Live Switching)**:
     - `LocalizationService` hiện thực `ILocalizationService` và `INotifyPropertyChanged`.
     - Khi `SetCulture` được gọi: cập nhật `CultureInfo.CurrentCulture`, `CultureInfo.CurrentUICulture`, `CultureInfo.DefaultThreadCurrentCulture`, `CultureInfo.DefaultThreadCurrentUICulture`, đồng thời phát sinh sự kiện `PropertyChanged(null)` và `CultureChanged`.
     - Cung cấp `LocExtension` trong XAML: `<TextBlock Text="{loc:Loc settings.language.title}" />` tự động làm mới giao diện ngay lập tức mà không cần khởi động lại ứng dụng.
  4. **Tổ chức kiểm thử**:
     - Thêm project `tests/LunarCal.App.Tests/` (`net10.0-windows10.0.19041.0`, WPF enabled) để unit test cho các service và view model của App.
- **Phương án đã loại:**
  - Chỉ dùng `Strings.vi-VN.resx` và `Strings.en-US.resx` mà không có `Strings.resx`: `ResourceManager` của .NET sẽ ném `MissingManifestResourceException` khi tìm kiếm neutral fallback trong assembly chính.
- **Hệ quả:**
  - Đáp ứng trọn vẹn FR-I18N-1, FR-I18N-3, FR-I18N-7 và NFR-MAINT.
  - Sẵn sàng cung cấp hạ tầng chuỗi cho các view model và shell giao diện ở các task tiếp theo của Phase 2.
