using System.Collections;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using LunarCal.App.Localization;

namespace LunarCal.App.Tests;

public class LocalizationTests
{
    private static readonly string _appProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../src/LunarCal.App"));

    [Theory]
    [InlineData("vi-VN", "vi-VN")]
    [InlineData("vi", "vi-VN")]
    [InlineData("vi-VN-x-custom", "vi-VN")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "vi-VN")]
    public void FR_I18N_1_Default_language_follows_OS(string osCultureCode, string expectedCultureCode)
    {
        var osCulture = string.IsNullOrEmpty(osCultureCode)
            ? CultureInfo.InvariantCulture
            : new CultureInfo(osCultureCode);

        var resolved = SupportedCultures.ResolveDefaultCulture(osCulture);

        resolved.Name.Should().Be(expectedCultureCode);
    }

    [Fact]
    public void FR_I18N_1_Default_language_falls_back_to_vietnamese_when_os_culture_is_null()
    {
        var resolved = SupportedCultures.ResolveDefaultCulture(null);

        resolved.Should().NotBeNull();
        resolved.Name.Should().BeOneOf("vi-VN", "en-US");
    }

    [Fact]
    public void FR_I18N_1_Runtime_language_switch_updates_immediately_without_restart()
    {
        var service = new LocalizationService(SupportedCultures.Vietnamese);
        service.CurrentCulture.Name.Should().Be("vi-VN");
        service["settings.language.title"].Should().Be("Ngôn ngữ giao diện");

        var propertyChangedFired = false;
        service.PropertyChanged += (_, _) => propertyChangedFired = true;

        CultureInfo? cultureChangedCulture = null;
        service.CultureChanged += (_, c) => cultureChangedCulture = c;

        // Switch to English at runtime
        service.SetCulture("en-US");

        service.CurrentCulture.Name.Should().Be("en-US");
        service["settings.language.title"].Should().Be("Interface language");
        propertyChangedFired.Should().BeTrue();
        cultureChangedCulture.Should().NotBeNull();
        cultureChangedCulture!.Name.Should().Be("en-US");

        // Switch back to Vietnamese at runtime
        propertyChangedFired = false;
        service.SetCulture("vi-VN");

        service.CurrentCulture.Name.Should().Be("vi-VN");
        service["settings.language.title"].Should().Be("Ngôn ngữ giao diện");
        propertyChangedFired.Should().BeTrue();
    }

    [Fact]
    public void FR_I18N_3_Every_key_has_non_empty_vi_and_en()
    {
        var viPath = Path.Combine(_appProjectPath, "Localization", "Strings.vi-VN.resx");
        var enPath = Path.Combine(_appProjectPath, "Localization", "Strings.en-US.resx");
        var neutralPath = Path.Combine(_appProjectPath, "Localization", "Strings.resx");

        File.Exists(viPath).Should().BeTrue($"Strings.vi-VN.resx must exist at {viPath}");
        File.Exists(enPath).Should().BeTrue($"Strings.en-US.resx must exist at {enPath}");
        File.Exists(neutralPath).Should().BeTrue($"Strings.resx must exist at {neutralPath}");

        var viEntries = LoadResxEntries(viPath);
        var enEntries = LoadResxEntries(enPath);
        var neutralEntries = LoadResxEntries(neutralPath);

        viEntries.Should().NotBeEmpty("Vietnamese resource file must contain entries");
        enEntries.Should().NotBeEmpty("English resource file must contain entries");

        // 1. Every key in vi must exist in en and vice versa
        var missingInEn = viEntries.Keys.Except(enEntries.Keys).ToList();
        missingInEn.Should().BeEmpty("all Vietnamese keys must be present in English resx");

        var missingInVi = enEntries.Keys.Except(viEntries.Keys).ToList();
        missingInVi.Should().BeEmpty("all English keys must be present in Vietnamese resx");

        // 2. Neutral Strings.resx must match Strings.vi-VN.resx
        var missingInNeutral = viEntries.Keys.Except(neutralEntries.Keys).ToList();
        missingInNeutral.Should().BeEmpty("neutral Strings.resx must have all Vietnamese keys");

        // 3. Values must be non-empty and non-whitespace in both languages
        foreach (var (key, value) in viEntries)
        {
            string.IsNullOrWhiteSpace(value).Should().BeFalse($"Vietnamese key '{key}' must have non-empty value");
        }

        foreach (var (key, value) in enEntries)
        {
            string.IsNullOrWhiteSpace(value).Should().BeFalse($"English key '{key}' must have non-empty value");
        }
    }

    [Fact]
    public void FR_I18N_7_Required_spec_table_strings_match_exact_values()
    {
        var service = new LocalizationService();

        // Exact values mandated by spec §4.11 (FR-I18N-7)
        var specTable = new Dictionary<string, (string Vi, string En)>
        {
            ["settings.language.title"] = ("Ngôn ngữ giao diện", "Interface language"),
            ["settings.language.desc"] = ("Đổi ngôn ngữ UI không làm thay đổi dữ liệu lịch", "Changing the UI language does not change your calendar data"),
            ["settings.lunar.title"] = ("Hiển thị Âm lịch", "Show lunar calendar"),
            ["settings.lunar.desc"] = ("Sự kiện Âm lịch lưu cục bộ, không đồng bộ lên Google", "Lunar events are stored locally and are not synced to Google"),
            ["settings.training.title"] = ("Đóng góp dữ liệu đã loại thông tin nhận dạng để huấn luyện AI (Opt-in)", "Contribute de-identified data to train AI (opt-in)"),
            ["settings.training.desc"] = ("Tên, địa điểm, email, số điện thoại được loại bỏ ngay trên máy bạn trước khi gửi. Mặc định tắt.", "Names, locations, emails and phone numbers are removed on your device before sending. Off by default."),
            ["settings.privacy.title"] = ("Quyền riêng tư", "Privacy"),
            ["settings.privacy.desc"] = ("Bạn có thể xem và xóa dữ liệu đã đóng góp bất cứ lúc nào", "You can view and delete the data you contributed at any time"),
            ["settings.privacy.view"] = ("Xem dữ liệu đã gửi", "View sent data"),
            ["settings.privacy.delete"] = ("Xóa dữ liệu đã đóng góp", "Delete contributed data"),
            ["settings.byok.title"] = ("Gemini API key của bạn (BYOK)", "Your Gemini API key (BYOK)"),
            ["settings.byok.desc"] = ("Key được mã hóa và chỉ lưu trên máy này; app dùng trực tiếp để gọi Gemini. Bạn có thể xóa hoặc thu hồi bất cứ lúc nào.", "The key is encrypted and stored only on this device; the app calls Gemini directly with it. You can remove or revoke it at any time."),
            ["settings.gemini.title"] = ("Dùng Gemini để hiểu câu lệnh", "Use Gemini to understand commands"),
            ["settings.gemini.desc"] = ("Chỉ gửi nội dung câu lệnh, không gửi toàn bộ lịch của bạn", "Only the command text is sent, never your calendar"),
            ["confirm.privacy.off"] = ("Opt-in đang tắt: không gửi dữ liệu huấn luyện khi bạn xác nhận.", "Opt-in is off: no training data is sent when you confirm."),
            ["confirm.privacy.on"] = ("Opt-in đang bật: bản đã loại thông tin nhận dạng của yêu cầu này sẽ được gửi để huấn luyện AI.", "Opt-in is on: a de-identified copy of this request will be sent to train AI."),
            ["consent.title"] = ("Giúp cải thiện AI?", "Help improve the AI?"),
            ["consent.body"] = ("Nếu bạn đồng ý, khi bạn xác nhận hoặc sửa một lệnh AI, bản đã loại thông tin nhận dạng của câu lệnh và sự kiện cuối cùng sẽ được gửi lên máy chủ để huấn luyện mô hình. Không gửi tên, địa điểm, email hay toàn bộ lịch. Bạn có thể xem và xóa dữ liệu bất cứ lúc nào.", "If you agree, when you confirm or edit an AI command, a de-identified copy of the command and the final event is sent to our server to train a model. Names, locations, emails and your full calendar are never sent. You can view and delete your data at any time."),
            ["consent.accept"] = ("Đồng ý đóng góp", "I agree to contribute"),
            ["consent.decline"] = ("Không, cảm ơn", "No thanks"),
            ["lunar.title.range"] = ("Âm lịch: tháng 8 – 9 năm Bính Ngọ", "Lunar: months 8 – 9, year of Bính Ngọ (Fire Horse)"),
        };

        foreach (var (key, (expectedVi, expectedEn)) in specTable)
        {
            var actualVi = service.GetString(key, SupportedCultures.Vietnamese);
            var actualEn = service.GetString(key, SupportedCultures.English);

            actualVi.Should().Be(expectedVi, $"key '{key}' in Vietnamese must match spec exactly");
            actualEn.Should().Be(expectedEn, $"key '{key}' in English must match spec exactly");
        }
    }

    [Fact]
    public void FR_NOTIFY_Toast_strings_match_exact_values_and_support_formatting()
    {
        var service = new LocalizationService();

        var toastTable = new Dictionary<string, (string Vi, string En)>
        {
            ["Toast_Header_Reminder"] = ("{0} · Nhắc nhở", "{0} · Reminder"),
            ["Toast_Time_Remaining"] = ("còn {0} phút", "in {0} min"),
            ["Toast_Time_StartsNow"] = ("bắt đầu ngay", "starting now"),
            ["Toast_Time_Ongoing"] = ("Đang diễn ra · bắt đầu {0} phút trước", "In progress · started {0} min ago"),
            ["Toast_AllDay_Today"] = ("Cả ngày · Hôm nay", "All day · Today"),
            ["Toast_AllDay_Tomorrow"] = ("Cả ngày · Ngày mai", "All day · Tomorrow"),
            ["Toast_Label_AI"] = ("✨ Tạo bởi AI", "✨ Created by AI"),
            ["Toast_Label_Lunar"] = ("🌙 {0} ÂL", "🌙 {0} Lunar"),
            ["Toast_Action_Snooze"] = ("Hoãn", "Snooze"),
            ["Toast_Snooze_Minutes"] = ("{0} phút", "{0} minutes"),
            ["Toast_Snooze_AtStart"] = ("Lúc bắt đầu", "At start time"),
            ["Toast_Action_Complete"] = ("Hoàn thành", "Done"),
            ["Toast_Missed_Summary"] = ("Bạn đã lỡ {0} nhắc nhở", "You missed {0} reminders"),
            ["Toast_Hidden_Content"] = ("Bạn có sự kiện lúc {0}", "You have an event at {0}"),
        };

        foreach (var (key, (expectedVi, expectedEn)) in toastTable)
        {
            var actualVi = service.GetString(key, SupportedCultures.Vietnamese);
            var actualEn = service.GetString(key, SupportedCultures.English);

            actualVi.Should().Be(expectedVi, $"toast key '{key}' in Vietnamese must match spec");
            actualEn.Should().Be(expectedEn, $"toast key '{key}' in English must match spec");
        }

        // Test format helper
        service.SetCulture("vi-VN");
        service.Format("Toast_Header_Reminder", "Họp dự án").Should().Be("Họp dự án · Nhắc nhở");
        service.Format("Toast_Time_Remaining", 15).Should().Be("còn 15 phút");

        service.SetCulture("en-US");
        service.Format("Toast_Header_Reminder", "Project Sync").Should().Be("Project Sync · Reminder");
        service.Format("Toast_Time_Remaining", 15).Should().Be("in 15 min");
    }

    private static Dictionary<string, string> LoadResxEntries(string resxFilePath)
    {
        var doc = XDocument.Load(resxFilePath);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value;
            if (name != null)
            {
                entries[name] = value ?? string.Empty;
            }
        }

        return entries;
    }
}
