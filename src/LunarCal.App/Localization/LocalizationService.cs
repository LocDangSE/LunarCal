using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace LunarCal.App.Localization;

/// <summary>
/// Live localization service using the embedded and satellite <c>Strings.*.resx</c> resources (FR-I18N-1, FR-I18N-3).
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager _resourceManager =
        new("LunarCal.App.Localization.Strings", typeof(LocalizationService).Assembly);

    private static LocalizationService? _instance;

    /// <summary>Singleton instance for XAML markup access or DI-less binding.</summary>
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private CultureInfo _currentCulture;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CultureInfo>? CultureChanged;

    public LocalizationService()
        : this(SupportedCultures.ResolveDefaultCulture())
    {
    }

    public LocalizationService(CultureInfo initialCulture)
    {
        _currentCulture = SupportedCultures.Normalize(initialCulture?.Name);
        ApplyThreadCultures(_currentCulture);
    }

    public CultureInfo CurrentCulture => _currentCulture;

    public string this[string key] => GetString(key, _currentCulture);

    public void SetCulture(string cultureCode)
    {
        SetCulture(SupportedCultures.Normalize(cultureCode));
    }

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var normalized = SupportedCultures.Normalize(culture.Name);

        if (Equals(_currentCulture, normalized))
        {
            return;
        }

        _currentCulture = normalized;
        ApplyThreadCultures(_currentCulture);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        CultureChanged?.Invoke(this, _currentCulture);
    }

    public string GetString(string key)
    {
        return GetString(key, _currentCulture);
    }

    public string GetString(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        var value = _resourceManager.GetString(key, culture);
        return value ?? $"!{key}!";
    }

    public string Format(string key, params object[] args)
    {
        var pattern = GetString(key, _currentCulture);
        return string.Format(_currentCulture, pattern, args);
    }

    private static void ApplyThreadCultures(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
