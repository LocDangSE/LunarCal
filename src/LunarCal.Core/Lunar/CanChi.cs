namespace LunarCal.Core.Lunar;

/// <summary>Heavenly stems (Thiên can). Member names are ASCII forms of the Vietnamese names.</summary>
public enum ThienCan
{
    /// <summary>Giáp</summary>
    Giap = 0,

    /// <summary>Ất</summary>
    At = 1,

    /// <summary>Bính</summary>
    Binh = 2,

    /// <summary>Đinh</summary>
    Dinh = 3,

    /// <summary>Mậu</summary>
    Mau = 4,

    /// <summary>Kỷ</summary>
    Ky = 5,

    /// <summary>Canh</summary>
    Canh = 6,

    /// <summary>Tân</summary>
    Tan = 7,

    /// <summary>Nhâm</summary>
    Nham = 8,

    /// <summary>Quý</summary>
    Quy = 9,
}

/// <summary>Earthly branches (Địa chi). Member names are ASCII forms of the Vietnamese names.</summary>
public enum DiaChi
{
    /// <summary>Tý (Rat)</summary>
    Ty = 0,

    /// <summary>Sửu (Buffalo)</summary>
    Suu = 1,

    /// <summary>Dần (Tiger)</summary>
    Dan = 2,

    /// <summary>Mão (Cat)</summary>
    Mao = 3,

    /// <summary>Thìn (Dragon)</summary>
    Thin = 4,

    /// <summary>Tỵ (Snake)</summary>
    Ti = 5,

    /// <summary>Ngọ (Horse)</summary>
    Ngo = 6,

    /// <summary>Mùi (Goat)</summary>
    Mui = 7,

    /// <summary>Thân (Monkey)</summary>
    Than = 8,

    /// <summary>Dậu (Rooster)</summary>
    Dau = 9,

    /// <summary>Tuất (Dog)</summary>
    Tuat = 10,

    /// <summary>Hợi (Pig)</summary>
    Hoi = 11,
}

/// <summary>
/// A stem-branch pair (Can Chi) of the sexagenary cycle.
/// <see cref="ToString"/> returns the Vietnamese name, which is shown in both UI languages
/// (FR-I18N-5); English glosses belong to the App's resources.
/// </summary>
public readonly record struct CanChi(ThienCan Can, DiaChi Chi)
{
    private static readonly string[] _canNames = ["Giáp", "Ất", "Bính", "Đinh", "Mậu", "Kỷ", "Canh", "Tân", "Nhâm", "Quý"];
    private static readonly string[] _chiNames = ["Tý", "Sửu", "Dần", "Mão", "Thìn", "Tỵ", "Ngọ", "Mùi", "Thân", "Dậu", "Tuất", "Hợi"];

    /// <summary>Position in the 60-term cycle, 0 = Giáp Tý … 59 = Quý Hợi.</summary>
    public int CycleIndex
    {
        get
        {
            // Solve i ≡ can (mod 10), i ≡ chi (mod 12). Only pairs with the same parity exist.
            for (var i = (int)Can; i < 60; i += 10)
            {
                if (i % 12 == (int)Chi)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Vietnamese name of the stem, e.g. "Bính".</summary>
    public string CanName => _canNames[(int)Can];

    /// <summary>Vietnamese name of the branch, e.g. "Ngọ".</summary>
    public string ChiName => _chiNames[(int)Chi];

    /// <summary>Creates the pair at a position of the 60-term cycle (0 = Giáp Tý).</summary>
    public static CanChi FromCycleIndex(int index)
    {
        var i = ((index % 60) + 60) % 60;
        return new CanChi((ThienCan)(i % 10), (DiaChi)(i % 12));
    }

    /// <summary>Vietnamese name, e.g. "Bính Ngọ".</summary>
    public override string ToString() => $"{CanName} {ChiName}";
}
