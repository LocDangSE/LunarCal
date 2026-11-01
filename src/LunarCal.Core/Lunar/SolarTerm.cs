namespace LunarCal.Core.Lunar;

/// <summary>
/// The 24 solar terms (tiết khí). The numeric value is the term's index in the ecliptic:
/// the term starts when the apparent sun longitude reaches <c>value × 15°</c>.
/// </summary>
public enum SolarTerm
{
    /// <summary>Xuân phân (Spring Equinox), 0°.</summary>
    XuanPhan = 0,

    /// <summary>Thanh minh (Pure Brightness), 15°.</summary>
    ThanhMinh = 1,

    /// <summary>Cốc vũ (Grain Rain), 30°.</summary>
    CocVu = 2,

    /// <summary>Lập hạ (Start of Summer), 45°.</summary>
    LapHa = 3,

    /// <summary>Tiểu mãn (Grain Buds), 60°.</summary>
    TieuMan = 4,

    /// <summary>Mang chủng (Grain in Ear), 75°.</summary>
    MangChung = 5,

    /// <summary>Hạ chí (Summer Solstice), 90°.</summary>
    HaChi = 6,

    /// <summary>Tiểu thử (Minor Heat), 105°.</summary>
    TieuThu = 7,

    /// <summary>Đại thử (Major Heat), 120°.</summary>
    DaiThu = 8,

    /// <summary>Lập thu (Start of Autumn), 135°.</summary>
    LapThu = 9,

    /// <summary>Xử thử (End of Heat), 150°.</summary>
    XuThu = 10,

    /// <summary>Bạch lộ (White Dew), 165°.</summary>
    BachLo = 11,

    /// <summary>Thu phân (Autumn Equinox), 180°.</summary>
    ThuPhan = 12,

    /// <summary>Hàn lộ (Cold Dew), 195°.</summary>
    HanLo = 13,

    /// <summary>Sương giáng (Frost's Descent), 210°.</summary>
    SuongGiang = 14,

    /// <summary>Lập đông (Start of Winter), 225°.</summary>
    LapDong = 15,

    /// <summary>Tiểu tuyết (Minor Snow), 240°.</summary>
    TieuTuyet = 16,

    /// <summary>Đại tuyết (Major Snow), 255°.</summary>
    DaiTuyet = 17,

    /// <summary>Đông chí (Winter Solstice), 270°.</summary>
    DongChi = 18,

    /// <summary>Tiểu hàn (Minor Cold), 285°.</summary>
    TieuHan = 19,

    /// <summary>Đại hàn (Major Cold), 300°.</summary>
    DaiHan = 20,

    /// <summary>Lập xuân (Start of Spring), 315°.</summary>
    LapXuan = 21,

    /// <summary>Vũ thủy (Rain Water), 330°.</summary>
    VuThuy = 22,

    /// <summary>Kinh trập (Awakening of Insects), 345°.</summary>
    KinhTrap = 23,
}

/// <summary>
/// The solar term in effect on a day. A term is attributed to the day (UTC+7) during which the sun
/// reaches its longitude; <see cref="IsStartDay"/> is true on that day (ADR-0007).
/// </summary>
public readonly record struct TietKhi(SolarTerm Term, bool IsStartDay)
{
    private static readonly string[] _names =
    [
        "Xuân phân", "Thanh minh", "Cốc vũ", "Lập hạ", "Tiểu mãn", "Mang chủng",
        "Hạ chí", "Tiểu thử", "Đại thử", "Lập thu", "Xử thử", "Bạch lộ",
        "Thu phân", "Hàn lộ", "Sương giáng", "Lập đông", "Tiểu tuyết", "Đại tuyết",
        "Đông chí", "Tiểu hàn", "Đại hàn", "Lập xuân", "Vũ thủy", "Kinh trập",
    ];

    /// <summary>Sun longitude (degrees) at which the term starts.</summary>
    public int LongitudeDegrees => (int)Term * 15;

    /// <summary>Vietnamese name, e.g. "Lập đông". The English name comes from the App's resources (FR-I18N-5).</summary>
    public string VietnameseName => _names[(int)Term];

    /// <inheritdoc cref="VietnameseName"/>
    public override string ToString() => VietnameseName;
}
