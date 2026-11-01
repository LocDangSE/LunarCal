using System.Globalization;
using LunarCal.Core.Settings;

namespace LunarCal.Core.Tests.Settings;

public class SettingsDefaultsTests
{
    [Theory]
    [InlineData("vi-VN", DayOfWeek.Monday)]
    [InlineData("vi", DayOfWeek.Monday)]
    [InlineData("vi-VN-x-custom", DayOfWeek.Monday)]
    [InlineData("en-US", DayOfWeek.Sunday)]
    [InlineData("en-GB", DayOfWeek.Sunday)]
    [InlineData("en", DayOfWeek.Sunday)]
    [InlineData("fr-FR", DayOfWeek.Sunday)]
    [InlineData("ja-JP", DayOfWeek.Sunday)]
    [InlineData("", DayOfWeek.Sunday)]
    [InlineData(null, DayOfWeek.Sunday)]
    public void FR_UI_7_Default_week_start_is_Monday_for_Vietnamese_and_Sunday_for_other_cultures(
        string? cultureCode,
        DayOfWeek expectedFirstDay)
    {
        var resolved = AppSettingDefaults.ResolveDefaultWeekStart(cultureCode);

        resolved.Should().Be(expectedFirstDay);
    }

    [Fact]
    public void FR_LUNAR_6_Default_lunar_enabled_is_true()
    {
        AppSettingDefaults.DefaultLunarEnabled.Should().BeTrue("lunar calendar master toggle must be on by default");
    }

    [Fact]
    public void FR_UI_7_Default_theme_is_System()
    {
        AppSettingDefaults.DefaultTheme.Should().Be("System", "UI theme must follow system setting by default");
    }

    [Fact]
    public void FR_UI_7_Default_timezone_matches_local_system_timezone()
    {
        var defaultTimezone = AppSettingDefaults.ResolveDefaultTimezone();

        defaultTimezone.Should().Be(TimeZoneInfo.Local.Id);
    }

    [Theory]
    [InlineData("vi-VN", "vi-VN")]
    [InlineData("vi", "vi-VN")]
    [InlineData("en-US", "en-US")]
    [InlineData("fr-FR", "en-US")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    public void FR_I18N_1_Default_ui_language_follows_os_culture(string? osCultureName, string expectedLanguage)
    {
        var resolved = AppSettingDefaults.ResolveDefaultUiLanguage(osCultureName);

        resolved.Should().Be(expectedLanguage);
    }

    [Fact]
    public void Initial_defaults_dictionary_contains_all_spec_keys()
    {
        var defaultsVi = AppSettingDefaults.CreateInitialDefaults("vi-VN");
        defaultsVi[SettingKeys.UiLanguage].Should().Be("vi-VN");
        defaultsVi[SettingKeys.LunarEnabled].Should().Be("true");
        defaultsVi[SettingKeys.WeekStart].Should().Be("Monday");
        defaultsVi[SettingKeys.Theme].Should().Be("System");
        defaultsVi[SettingKeys.Timezone].Should().Be(TimeZoneInfo.Local.Id);
        defaultsVi[SettingKeys.GeminiEnabled].Should().Be("true");
        defaultsVi[SettingKeys.TrainingOptIn].Should().Be("false");

        var defaultsEn = AppSettingDefaults.CreateInitialDefaults("en-US");
        defaultsEn[SettingKeys.UiLanguage].Should().Be("en-US");
        defaultsEn[SettingKeys.LunarEnabled].Should().Be("true");
        defaultsEn[SettingKeys.WeekStart].Should().Be("Sunday");
    }
}
