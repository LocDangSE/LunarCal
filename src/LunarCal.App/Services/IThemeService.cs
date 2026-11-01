namespace LunarCal.App.Services;

/// <summary>
/// Resolved active visual theme mode applied to the application (FR-UI-7, FR-UI-8).
/// </summary>
public enum AppTheme
{
    /// <summary>Light theme palette.</summary>
    Light,

    /// <summary>Dark theme palette.</summary>
    Dark,

    /// <summary>High contrast theme palette conforming to Windows accessibility (FR-UI-8).</summary>
    HighContrast,
}

/// <summary>
/// Abstraction for detecting the host operating system's theme and high contrast settings.
/// Allows deterministic mocking and unit testing without depending on the Windows registry.
/// </summary>
public interface ISystemThemeDetector
{
    /// <summary>Whether high contrast mode is enabled at the OS level.</summary>
    bool IsHighContrast { get; }

    /// <summary>Whether the OS is configured for dark app mode.</summary>
    bool IsDarkTheme { get; }
}

/// <summary>
/// Service managing runtime theme switching, system theme tracking, and dynamic ResourceDictionary synchronization (FR-UI-7, FR-UI-8).
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Current user preference setting: "Light", "Dark", or "System".
    /// </summary>
    string CurrentThemeSetting { get; }

    /// <summary>
    /// Current effectively applied theme.
    /// </summary>
    AppTheme ActiveTheme { get; }

    /// <summary>
    /// Whether high contrast mode is currently active.
    /// </summary>
    bool IsHighContrast { get; }

    /// <summary>
    /// Applies the specified theme setting ("Light", "Dark", "System") and updates the application resources dynamically.
    /// </summary>
    /// <param name="themeSetting">Theme setting name.</param>
    void ApplyTheme(string themeSetting);

    /// <summary>
    /// Raised whenever the effective <see cref="ActiveTheme"/> changes.
    /// </summary>
    event EventHandler<AppTheme>? ThemeChanged;
}
