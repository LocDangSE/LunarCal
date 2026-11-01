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
    private bool _isPseudoLocaleEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CultureInfo>? CultureChanged;

    public LocalizationService()
        : this(SupportedCultures.ResolveDefaultCulture(), enablePseudoLocale: false)
    {
    }

    public LocalizationService(CultureInfo initialCulture, bool enablePseudoLocale = false)
    {
        _currentCulture = SupportedCultures.Normalize(initialCulture?.Name);
        _isPseudoLocaleEnabled = enablePseudoLocale;
        ApplyThreadCultures(_currentCulture);
    }

    public CultureInfo CurrentCulture => _currentCulture;

    public bool IsPseudoLocaleEnabled
    {
        get => _isPseudoLocaleEnabled;
        set
        {
            if (_isPseudoLocaleEnabled == value)
            {
                return;
            }

            _isPseudoLocaleEnabled = value;
            NotifyChanges();
        }
    }

    public void Configure(LocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IsPseudoLocaleEnabled = options.EnablePseudoLocale;
    }

    public string this[string key] => GetString(key, _currentCulture);

    public void SetCulture(string cultureCode)
    {
        SetCulture(SupportedCultures.Normalize(cultureCode));
    }

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var normalized = SupportedCultures.Normalize(culture.Name);

        if (normalized.Name.Equals(SupportedCultures.PseudoLocaleCode, StringComparison.OrdinalIgnoreCase))
        {
            _currentCulture = normalized;
            _isPseudoLocaleEnabled = true;
            ApplyThreadCultures(_currentCulture);
            NotifyChanges();
            return;
        }

        if (Equals(_currentCulture, normalized))
        {
            return;
        }

        _currentCulture = normalized;
        ApplyThreadCultures(_currentCulture);
        NotifyChanges();
    }

    public string GetString(string key)
    {
        return GetString(key, _currentCulture);
    }

    public string GetString(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);

        var isPseudo = _isPseudoLocaleEnabled || culture.Name.Equals(SupportedCultures.PseudoLocaleCode, StringComparison.OrdinalIgnoreCase);

        // When querying resource manager for pseudo-locale, look up against current or neutral language
        var lookupCulture = culture.Name.Equals(SupportedCultures.PseudoLocaleCode, StringComparison.OrdinalIgnoreCase)
            ? SupportedCultures.Vietnamese
            : culture;

        var value = _resourceManager.GetString(key, lookupCulture);
        if (value is null)
        {
            return $"!{key}!";
        }

        return isPseudo ? PseudoLocalizer.Transform(value) : value;
    }

    public string Format(string key, params object[] args)
    {
        var pattern = GetString(key, _currentCulture);
        return string.Format(_currentCulture, pattern, args);
    }

    private void NotifyChanges()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        CultureChanged?.Invoke(this, _currentCulture);
    }

    private static void ApplyThreadCultures(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
