using System.Windows;
using Microsoft.Win32;

namespace LunarCal.App.Services;

/// <summary>
/// Default implementation of <see cref="ISystemThemeDetector"/> using Windows OS APIs and registry.
/// </summary>
public sealed class WindowsSystemThemeDetector : ISystemThemeDetector
{
    /// <inheritdoc/>
    public bool IsHighContrast => SystemParameters.HighContrast;

    /// <inheritdoc/>
    public bool IsDarkTheme
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int lightThemeValue)
                {
                    return lightThemeValue == 0;
                }
            }
            catch
            {
                // Fallback to light theme if registry is inaccessible
            }

            return false;
        }
    }
}

/// <summary>
/// Theme management service handling runtime theme switching and system theme tracking (FR-UI-7, FR-UI-8).
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISystemThemeDetector _systemThemeDetector;
    private string _currentThemeSetting = "System";
    private AppTheme _activeTheme = AppTheme.Light;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="ThemeService"/> with the default or specified detector.
    /// </summary>
    /// <param name="systemThemeDetector">Optional custom system theme detector for testing.</param>
    public ThemeService(ISystemThemeDetector? systemThemeDetector = null)
    {
        _systemThemeDetector = systemThemeDetector ?? new WindowsSystemThemeDetector();

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // SystemEvents may be unavailable in some non-interactive environments
        }

        ApplyTheme("System");
    }

    /// <inheritdoc/>
    public string CurrentThemeSetting => _currentThemeSetting;

    /// <inheritdoc/>
    public AppTheme ActiveTheme => _activeTheme;

    /// <inheritdoc/>
    public bool IsHighContrast => _systemThemeDetector.IsHighContrast;

    /// <inheritdoc/>
    public event EventHandler<AppTheme>? ThemeChanged;

    /// <inheritdoc/>
    public void ApplyTheme(string themeSetting)
    {
        _currentThemeSetting = string.IsNullOrWhiteSpace(themeSetting) ? "System" : themeSetting.Trim();

        var resolvedTheme = ResolveTargetTheme(_currentThemeSetting);
        var themeChanged = _activeTheme != resolvedTheme;
        _activeTheme = resolvedTheme;

        UpdateApplicationResources(resolvedTheme);

        if (themeChanged)
        {
            ThemeChanged?.Invoke(this, resolvedTheme);
        }
    }

    /// <summary>
    /// Computes the target <see cref="AppTheme"/> based on user preference and OS detection.
    /// </summary>
    public AppTheme ResolveTargetTheme(string themeSetting)
    {
        if (_systemThemeDetector.IsHighContrast)
        {
            return AppTheme.HighContrast;
        }

        if (string.Equals(themeSetting, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return AppTheme.Dark;
        }

        if (string.Equals(themeSetting, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return AppTheme.Light;
        }

        // "System" mode: follow OS preference
        return _systemThemeDetector.IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
    }

    /// <summary>
    /// Gets the pack URI for the specified theme resource dictionary.
    /// </summary>
    public static Uri GetThemeUri(AppTheme theme)
    {
        var relativePath = theme switch
        {
            AppTheme.Dark => "Themes/Dark.xaml",
            AppTheme.HighContrast => "Themes/HighContrast.xaml",
            _ => "Themes/Light.xaml",
        };

        return new Uri($"pack://application:,,,/LunarCal;component/{relativePath}", UriKind.Absolute);
    }

    private static void UpdateApplicationResources(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        try
        {
            var targetUri = GetThemeUri(theme);
            var merged = app.Resources.MergedDictionaries;

            int themeIndex = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                var source = merged[i].Source?.OriginalString;
                if (source != null &&
                    (source.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                     source.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                     source.Contains("Themes/HighContrast.xaml", StringComparison.OrdinalIgnoreCase)))
                {
                    themeIndex = i;
                    break;
                }
            }

            var newDict = new ResourceDictionary { Source = targetUri };

            if (themeIndex >= 0)
            {
                merged[themeIndex] = newDict;
            }
            else
            {
                merged.Add(newDict);
            }
        }
        catch
        {
            // Resource swapping may be bypassed in headless unit test contexts
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            if (string.Equals(_currentThemeSetting, "System", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTheme("System");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // Ignore during disposal
        }

        _disposed = true;
    }
}
