using System.ComponentModel;
using System.Globalization;

namespace LunarCal.App.Localization;

/// <summary>
/// Service managing the active UI culture, resource string lookups, live culture switching and pseudo-localization (FR-I18N-1, FR-I18N-3).
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>The currently active UI culture (vi-VN, en-US, or qps-ploc).</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Gets or sets whether pseudo-localization is enabled for layout overflow testing (FR-I18N-3).</summary>
    bool IsPseudoLocaleEnabled { get; set; }

    /// <summary>Configures localization service using <see cref="LocalizationOptions"/>.</summary>
    void Configure(LocalizationOptions options);

    /// <summary>Sets the active UI culture without restarting the app (FR-I18N-1).</summary>
    void SetCulture(CultureInfo culture);

    /// <summary>Sets the active UI culture by code ("vi-VN", "en-US", "qps-ploc").</summary>
    void SetCulture(string cultureCode);

    /// <summary>Gets a localized string for the specified key in the current UI culture.</summary>
    string GetString(string key);

    /// <summary>Gets a localized string for the specified key in an explicit culture.</summary>
    string GetString(string key, CultureInfo culture);

    /// <summary>Formats a localized string using <see cref="string.Format(IFormatProvider?, string, object?[])"/>.</summary>
    string Format(string key, params object[] args);

    /// <summary>Indexer for WPF XAML bindings: {Binding [key], Source={StaticResource Loc}}.</summary>
    string this[string key] { get; }

    /// <summary>Fired whenever the active culture changes.</summary>
    event EventHandler<CultureInfo>? CultureChanged;
}
