using System.Globalization;
using LunarCal.Core.Lunar;

namespace LunarCal.Core.Tests.Lunar;

/// <summary>Loads the CSV reference tables in <c>Fixtures/</c> (sources in Fixtures/README.md).</summary>
internal static class Fixtures
{
    private static readonly Lazy<IReadOnlyList<(DateOnly Date, LunarDate Lunar)>> _hkoMonthStarts =
        new(() => ReadMonthRows("hko-month-starts.csv"));

    private static readonly Lazy<IReadOnlyList<(DateOnly Date, LunarDate Lunar)>> _leapDifferences =
        new(() => ReadMonthRows("vn-hko-leap-differences.csv"));

    private static readonly Lazy<IReadOnlyList<(DateOnly Date, SolarTerm Term)>> _hkoSolarTerms =
        new(() => [.. ReadRows("hko-solar-terms.csv").Select(p => (ParseDate(p[0]), Enum.Parse<SolarTerm>(p[1])))]);

    /// <summary>First day of every lunar month 1901–2100 according to the Hong Kong Observatory (UTC+8).</summary>
    public static IReadOnlyList<(DateOnly Date, LunarDate Lunar)> HkoMonthStarts => _hkoMonthStarts.Value;

    /// <summary>HKO month starts at which the Vietnamese leap month differs; value = Vietnamese month starting that day.</summary>
    public static IReadOnlyList<(DateOnly Date, LunarDate Lunar)> LeapDifferences => _leapDifferences.Value;

    /// <summary>Start day of every solar term 1901–2100 according to the Hong Kong Observatory (UTC+8).</summary>
    public static IReadOnlyList<(DateOnly Date, SolarTerm Term)> HkoSolarTerms => _hkoSolarTerms.Value;

    public static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<(DateOnly, LunarDate)> ReadMonthRows(string file) =>
    [
        .. ReadRows(file).Select(p => (
            ParseDate(p[0]),
            new LunarDate(
                int.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture),
                1,
                bool.Parse(p[3])))),
    ];

    private static IEnumerable<string[]> ReadRows(string file) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Skip(1) // header
            .Select(line => line.Split(','));
}
