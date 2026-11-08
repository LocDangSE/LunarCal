namespace LunarCal.App.ViewModels;

/// <summary>
/// Settings navigation entries (Figma 04, S13, S20). "Thông báo" (Phase 7) and "Giới thiệu" (Phase 8) are added with their phases.
/// </summary>
public enum SettingsTab
{
    /// <summary>Chung: language, time zone, lunar calendar (Figma 04 section "Chung").</summary>
    General,

    /// <summary>Tài khoản Google (Figma 04 section "Tài khoản Google").</summary>
    GoogleAccount,

    /// <summary>AI và dữ liệu huấn luyện (Figma 04, FR-BYOK, P4-03, P6a-01).</summary>
    Ai,

    /// <summary>Giao diện (S13).</summary>
    Appearance,

    /// <summary>Nâng cao (S20).</summary>
    Advanced,
}
