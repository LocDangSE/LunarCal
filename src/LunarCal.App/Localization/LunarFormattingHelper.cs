using LunarCal.Core.Lunar;

namespace LunarCal.App.Localization;

/// <summary>
/// Helper for formatting lunar calendar names, Can Chi, and solar terms with bilingual glosses (FR-I18N-5, ADR-0008).
/// </summary>
public static class LunarFormattingHelper
{
    /// <summary>
    /// Gets the localized heavenly stem name or gloss from resources.
    /// </summary>
    public static string GetCanName(ThienCan can, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var key = $"can.{can.ToString().ToLowerInvariant()}";
        return loc[key];
    }

    /// <summary>
    /// Gets the localized earthly branch name or gloss from resources.
    /// </summary>
    public static string GetChiName(DiaChi chi, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var key = $"chi.{chi.ToString().ToLowerInvariant()}";
        return loc[key];
    }

    /// <summary>
    /// Maps a <see cref="SolarTerm"/> enum to its resx resource key.
    /// </summary>
    public static string GetSolarTermKey(SolarTerm term) => term switch
    {
        SolarTerm.XuanPhan => "solarterm.xuan_phan",
        SolarTerm.ThanhMinh => "solarterm.thanh_minh",
        SolarTerm.CocVu => "solarterm.coc_vu",
        SolarTerm.LapHa => "solarterm.lap_ha",
        SolarTerm.TieuMan => "solarterm.tieu_man",
        SolarTerm.MangChung => "solarterm.mang_chung",
        SolarTerm.HaChi => "solarterm.ha_chi",
        SolarTerm.TieuThu => "solarterm.tieu_thu",
        SolarTerm.DaiThu => "solarterm.dai_thu",
        SolarTerm.LapThu => "solarterm.lap_thu",
        SolarTerm.XuThu => "solarterm.xu_thu",
        SolarTerm.BachLo => "solarterm.bach_lo",
        SolarTerm.ThuPhan => "solarterm.thu_phan",
        SolarTerm.HanLo => "solarterm.han_lo",
        SolarTerm.SuongGiang => "solarterm.suong_giang",
        SolarTerm.LapDong => "solarterm.lap_dong",
        SolarTerm.TieuTuyet => "solarterm.tieu_tuyet",
        SolarTerm.DaiTuyet => "solarterm.dai_tuyet",
        SolarTerm.DongChi => "solarterm.dong_chi",
        SolarTerm.TieuHan => "solarterm.tieu_han",
        SolarTerm.DaiHan => "solarterm.dai_han",
        SolarTerm.LapXuan => "solarterm.lap_xuan",
        SolarTerm.VuThuy => "solarterm.vu_thuy",
        SolarTerm.KinhTrap => "solarterm.kinh_trap",
        _ => string.Empty,
    };

    /// <summary>
    /// Gets the localized name of a solar term from resources.
    /// </summary>
    public static string GetSolarTermName(SolarTerm term, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var key = GetSolarTermKey(term);
        return string.IsNullOrEmpty(key) ? string.Empty : loc[key];
    }

    /// <summary>
    /// Gets the element name (Wood, Fire, Earth, Metal, Water) for a heavenly stem.
    /// </summary>
    public static string GetStemElement(ThienCan can) => can switch
    {
        ThienCan.Giap or ThienCan.At => "Wood",
        ThienCan.Binh or ThienCan.Dinh => "Fire",
        ThienCan.Mau or ThienCan.Ky => "Earth",
        ThienCan.Canh or ThienCan.Tan => "Metal",
        _ => "Water",
    };

    /// <summary>
    /// Formats a stem-branch pair with English gloss when in English UI mode (FR-I18N-5).
    /// E.g. "Quý Hợi" in Vietnamese, "Quý Hợi (Yin Water Pig)" in English.
    /// </summary>
    public static string FormatCanChi(CanChi canChi, ILocalizationService loc, bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (!isEnglish)
        {
            return canChi.ToString();
        }

        var can = GetCanName(canChi.Can, loc);
        var chi = GetChiName(canChi.Chi, loc);
        return $"{canChi} ({can} {chi})";
    }

    /// <summary>
    /// Formats the lunar year Can Chi, adding "Year of the [Element] [Animal]" in English mode (FR-I18N-5).
    /// E.g. "Bính Ngọ" in Vietnamese, "Bính Ngọ (Year of the Fire Horse)" in English.
    /// </summary>
    public static string FormatYearCanChi(CanChi yearCanChi, ILocalizationService loc, bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (!isEnglish)
        {
            return yearCanChi.ToString();
        }

        var element = GetStemElement(yearCanChi.Can);
        var animal = GetChiName(yearCanChi.Chi, loc);
        return $"{yearCanChi} (Year of the {element} {animal})";
    }

    /// <summary>
    /// Formats a solar term, showing Vietnamese name and English gloss (FR-I18N-5).
    /// E.g. "Tiết Hàn lộ" in Vietnamese, "Hàn lộ (Cold Dew)" in English.
    /// </summary>
    public static string FormatSolarTerm(TietKhi tietKhi, ILocalizationService loc, bool isEnglish)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (!isEnglish)
        {
            return $"Tiết {tietKhi.VietnameseName}";
        }

        var engName = GetSolarTermName(tietKhi.Term, loc);
        return $"{tietKhi.VietnameseName} ({engName})";
    }
}
