namespace LunarCal.App.Localization;

/// <summary>
/// Configuration options for localization and pseudo-localization (FR-I18N-3).
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "LunarCal:Localization";

    /// <summary>
    /// Gets or sets whether pseudo-localization is enabled to test layout expansion and accent rendering.
    /// </summary>
    public bool EnablePseudoLocale { get; set; }
}
