namespace LunarCal.Core.Settings;

/// <summary>
/// Keys for the AppSetting table (spec §6, FR-UI-7, FR-LUNAR-6).
/// </summary>
public static class SettingKeys
{
    /// <summary>Interface language: "vi-VN" or "en-US" (FR-I18N-1).</summary>
    public const string UiLanguage = "ui_language";

    /// <summary>Show lunar calendar master toggle: "true" or "false" (FR-LUNAR-6). Default: "true".</summary>
    public const string LunarEnabled = "lunar_enabled";

    /// <summary>First day of the week: "Monday" or "Sunday" (FR-UI-7). Default: Monday for vi-VN, Sunday for en-US.</summary>
    public const string WeekStart = "week_start";

    /// <summary>Interface theme: "System", "Light", "Dark", or "HighContrast" (FR-UI-7). Default: "System".</summary>
    public const string Theme = "theme";

    /// <summary>Display timezone identifier (FR-UI-7). Default: system local timezone.</summary>
    public const string Timezone = "timezone";

    /// <summary>Time format: "12h" or "24h" (FR-UI-7, FR-I18N-4). Default: "24h" for vi-VN, "12h" for en-US.</summary>
    public const string TimeFormat = "time_format";

    /// <summary>Use Gemini for command parsing: "true" or "false" (spec §7). Default: "true".</summary>
    public const string GeminiEnabled = "gemini_enabled";

    /// <summary>Contribute de-identified data for AI training: "true" or "false" (FR-DATA-1, FR-CLOUD-1). Default: "false".</summary>
    public const string TrainingOptIn = "training_opt_in";

    /// <summary>Version of privacy/consent text accepted (FR-CLOUD-5).</summary>
    public const string ConsentVersion = "consent_version";

    /// <summary>Pseudonymous contributor UUID (FR-CLOUD-3).</summary>
    public const string ContributorId = "contributor_id";
}
