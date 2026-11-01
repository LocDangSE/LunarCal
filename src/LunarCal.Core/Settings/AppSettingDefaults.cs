namespace LunarCal.Core.Settings;

/// <summary>
/// Resolves default application settings according to the user's culture and system environment (spec §4.11, §6, FR-UI-7, FR-LUNAR-6).
/// </summary>
public static class AppSettingDefaults
{
    /// <summary>Default lunar calendar master toggle: true (enabled) per FR-LUNAR-6.</summary>
    public const bool DefaultLunarEnabled = true;

    /// <summary>Default interface theme: "System" per FR-UI-7.</summary>
    public const string DefaultTheme = "System";

    /// <summary>Default Gemini command parsing toggle: true per spec §7.</summary>
    public const bool DefaultGeminiEnabled = true;

    /// <summary>Default training data contribution opt-in: false (off by default) per FR-DATA-1, FR-I18N-7.</summary>
    public const bool DefaultTrainingOptIn = false;

    /// <summary>
    /// Resolves the default first day of the week according to the UI culture (FR-UI-7, spec §4.11):
    /// Monday for Vietnamese ("vi-VN"), Sunday for English ("en-US") and other cultures.
    /// </summary>
    public static DayOfWeek ResolveDefaultWeekStart(string? cultureCode)
    {
        if (!string.IsNullOrWhiteSpace(cultureCode) &&
            cultureCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return DayOfWeek.Monday;
        }

        return DayOfWeek.Sunday;
    }

    /// <summary>
    /// Resolves the default UI language according to the OS culture name (FR-I18N-1):
    /// "vi-VN" if the OS culture is Vietnamese, otherwise "en-US".
    /// </summary>
    public static string ResolveDefaultUiLanguage(string? osCultureName)
    {
        if (!string.IsNullOrWhiteSpace(osCultureName) &&
            osCultureName.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return "vi-VN";
        }

        return "en-US";
    }

    /// <summary>
    /// Resolves the default local timezone identifier (FR-UI-7).
    /// </summary>
    public static string ResolveDefaultTimezone()
    {
        return TimeZoneInfo.Local.Id;
    }

    /// <summary>
    /// Resolves the default time format according to UI culture:
    /// "24h" for Vietnamese ("vi-VN"), "12h" for English ("en-US") (FR-UI-7, FR-I18N-4).
    /// </summary>
    public static string ResolveDefaultTimeFormat(string? cultureCode)
    {
        if (!string.IsNullOrWhiteSpace(cultureCode) &&
            cultureCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return "24h";
        }

        return "12h";
    }

    /// <summary>
    /// Creates the complete dictionary of initial default settings based on culture and environment.
    /// </summary>
    public static Dictionary<string, string> CreateInitialDefaults(string? uiLanguage = null, string? osCultureName = null)
    {
        var resolvedLanguage = uiLanguage ?? ResolveDefaultUiLanguage(osCultureName);
        var resolvedWeekStart = ResolveDefaultWeekStart(resolvedLanguage);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SettingKeys.UiLanguage] = resolvedLanguage,
            [SettingKeys.LunarEnabled] = DefaultLunarEnabled ? "true" : "false",
            [SettingKeys.WeekStart] = resolvedWeekStart.ToString(),
            [SettingKeys.Theme] = DefaultTheme,
            [SettingKeys.Timezone] = ResolveDefaultTimezone(),
            [SettingKeys.TimeFormat] = ResolveDefaultTimeFormat(resolvedLanguage),
            [SettingKeys.GeminiEnabled] = DefaultGeminiEnabled ? "true" : "false",
            [SettingKeys.TrainingOptIn] = DefaultTrainingOptIn ? "true" : "false",
        };
    }
}
