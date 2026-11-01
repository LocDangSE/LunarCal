# ADR-0010: WPF + MVVM (CommunityToolkit.Mvvm) + Generic Host

- **Status:** Accepted (2026-10-31). Spec §2 đã chốt cố định.
- **Bối cảnh:** App desktop chỉ chạy trên Windows, cần DI, Options, ILogger. Spec §2 cấm WinForms.
- **Quyết định:** Dùng WPF trên `net10.0-windows10.0.19041.0`, MVVM bằng `CommunityToolkit.Mvvm` 8.4.2, và `Microsoft.Extensions.Hosting` 10.0.14 (Generic Host) dựng trong `src/LunarCal.App/App.xaml.cs`. View model đăng ký vào DI; view nhận view model qua constructor (xem `src/LunarCal.App/Views/MainWindow.xaml.cs`).
- **Hệ quả:** Mọi service mới đều đăng ký qua DI (`AddLunarCalInfrastructure` trong `src/LunarCal.Infrastructure/DependencyInjection.cs`). Thư viện style riêng là quyết định khác (ADR-0011).
