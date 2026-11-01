using System.Globalization;

namespace LunarCal.App.Localization;

/// <summary>
/// Supported UI cultures of LunarCal (FR-I18N-1, FR-I18N-3).
/// </summary>
public static class SupportedCultures
{
    public const string VietnameseCode = "vi-VN";
    public const string EnglishCode = "en-US";
    public const string PseudoLocaleCode = "qps-ploc";

    public static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo(VietnameseCode);
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo(EnglishCode);
    public static readonly CultureInfo PseudoLocale = CultureInfo.GetCultureInfo(PseudoLocaleCode);

    public static readonly IReadOnlyList<CultureInfo> All = [Vietnamese, English];

    /// <summary>
    /// Resolves the default UI culture according to OS culture (FR-I18N-1):
    /// vi-VN if the OS culture is Vietnamese ("vi" / "vi-*"), otherwise en-US;
    /// if indeterminate (null, empty or InvariantCulture), falls back to vi-VN.
    /// </summary>
    public static CultureInfo ResolveDefaultCulture(CultureInfo? osCulture = null)
    {
        var os = osCulture ?? CultureInfo.InstalledUICulture ?? CultureInfo.CurrentUICulture;

        if (os == null || string.IsNullOrEmpty(os.Name) || Equals(os, CultureInfo.InvariantCulture))
        {
            return Vietnamese;
        }

        if (os.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ||
            os.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase))
        {
            return Vietnamese;
        }

        return English;
    }

    /// <summary>
    /// Normalizes any culture input to one of the supported cultures (vi-VN, en-US, or qps-ploc).
    /// </summary>
    public static CultureInfo Normalize(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return Vietnamese;
        }

        if (cultureCode.StartsWith("qps", StringComparison.OrdinalIgnoreCase))
        {
            return PseudoLocale;
        }

        if (cultureCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return Vietnamese;
        }

        if (cultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }

        return Vietnamese;
    }
}
