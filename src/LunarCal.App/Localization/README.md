# Localization

Hạ tầng song ngữ vi-VN và en-US theo FR-I18N-1..7 và ADR-0024.

## Các tệp tài nguyên
- `Strings.resx`: Tệp tài nguyên gốc/neutral (chứa tiếng Việt theo `<NeutralLanguage>vi-VN</NeutralLanguage>`).
- `Strings.vi-VN.resx`: Tài nguyên tiếng Việt.
- `Strings.en-US.resx`: Tài nguyên tiếng Anh.

Mọi key phải có giá trị không rỗng ở cả hai ngôn ngữ (được kiểm tra tự động bởi unit test `FR_I18N_3_Every_key_has_non_empty_vi_and_en`).

## Sử dụng trong C#
```csharp
var loc = LocalizationService.Instance; // hoặc inject ILocalizationService
string text = loc.GetString("settings.language.title");
string formatted = loc.Format("Toast_Header_Reminder", "Họp nhóm");
```

## Sử dụng trong XAML
```xml
xmlns:loc="clr-namespace:LunarCal.App.Localization"
...
<TextBlock Text="{loc:Loc settings.language.title}" />
```
Khi gọi `loc.SetCulture("en-US")` hoặc `loc.SetCulture("vi-VN")`, toàn bộ giao diện tự động cập nhật ngay lập tức mà không cần khởi động lại ứng dụng.
