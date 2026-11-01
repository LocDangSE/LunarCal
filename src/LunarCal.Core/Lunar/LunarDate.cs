using System.Globalization;

namespace LunarCal.Core.Lunar;

/// <summary>A date in the Vietnamese lunar calendar.</summary>
/// <param name="Year">Lunar year number (the Gregorian year in which its month 1 starts).</param>
/// <param name="Month">Month number, 1–12.</param>
/// <param name="Day">Day of the month, 1–30.</param>
/// <param name="IsLeapMonth">True for the leap (nhuận) month that repeats <paramref name="Month"/>.</param>
public readonly record struct LunarDate(int Year, int Month, int Day, bool IsLeapMonth)
{
    /// <summary>Culture-invariant form for logs and tests, e.g. "2023-02L-01" for leap month 2. Not a UI string.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}{(IsLeapMonth ? "L" : string.Empty)}-{Day:D2}");
}
