using System.Text;
using System.Text.RegularExpressions;

namespace LunarCal.App.Localization;

/// <summary>
/// Transforms localized strings into pseudo-localized versions for layout overflow testing (FR-I18N-3).
/// Expands text length by approximately 40%, adds diacritic accents, and wraps strings with delimiters [ ... ],
/// while preserving format placeholders such as {0}, {1}, etc.
/// </summary>
public static partial class PseudoLocalizer
{
    private static readonly Dictionary<char, char> _charMap = new()
    {
        ['a'] = 'å',
        ['A'] = 'Å',
        ['b'] = 'ƀ',
        ['B'] = 'Ɓ',
        ['c'] = 'ç',
        ['C'] = 'Ç',
        ['d'] = 'ð',
        ['D'] = 'Ð',
        ['e'] = 'é',
        ['E'] = 'É',
        ['f'] = 'ƒ',
        ['F'] = 'Ƒ',
        ['g'] = 'ĝ',
        ['G'] = 'Ĝ',
        ['h'] = 'ĥ',
        ['H'] = 'Ĥ',
        ['i'] = 'î',
        ['I'] = 'Î',
        ['j'] = 'ĵ',
        ['J'] = 'Ĵ',
        ['k'] = 'ƙ',
        ['K'] = 'Ƙ',
        ['l'] = 'ł',
        ['L'] = 'Ł',
        ['m'] = 'ɱ',
        ['M'] = 'Ṁ',
        ['n'] = 'ñ',
        ['N'] = 'Ñ',
        ['o'] = 'ö',
        ['O'] = 'Ö',
        ['p'] = 'þ',
        ['P'] = 'Þ',
        ['q'] = 'ǫ',
        ['Q'] = 'Ǫ',
        ['r'] = 'ř',
        ['R'] = 'Ř',
        ['s'] = 'š',
        ['S'] = 'Š',
        ['t'] = 'ţ',
        ['T'] = 'Ţ',
        ['u'] = 'û',
        ['U'] = 'Û',
        ['v'] = 'ṽ',
        ['V'] = 'Ṽ',
        ['w'] = 'ŵ',
        ['W'] = 'Ŵ',
        ['x'] = 'ẋ',
        ['X'] = 'Ẋ',
        ['y'] = 'ý',
        ['Y'] = 'Ý',
        ['z'] = 'ž',
        ['Z'] = 'Ž'
    };

    private static readonly HashSet<char> _vowels =
    [
        'a', 'e', 'i', 'o', 'u', 'y',
        'A', 'E', 'I', 'O', 'U', 'Y',
        'á', 'à', 'ả', 'ã', 'ạ', 'â', 'ă', 'ắ', 'ằ', 'ẳ', 'ẵ', 'ặ', 'ấ', 'ầ', 'ẩ', 'ẫ', 'ậ',
        'é', 'è', 'ẻ', 'ẽ', 'ẹ', 'ê', 'ế', 'ề', 'ể', 'ễ', 'ệ',
        'í', 'ì', 'ỉ', 'ĩ', 'ị',
        'ó', 'ò', 'ỏ', 'õ', 'ọ', 'ô', 'ơ', 'ố', 'ồ', 'ổ', 'ỗ', 'ộ', 'ớ', 'ờ', 'ở', 'ỡ', 'ợ',
        'ú', 'ù', 'ủ', 'ũ', 'ụ', 'ư', 'ứ', 'ừ', 'ử', 'ữ', 'ự',
        'ý', 'ỳ', 'ỷ', 'ỹ', 'ỵ',
        'Á', 'À', 'Ả', 'Ã', 'Ạ', 'Â', 'Ă', 'Ấ', 'Ầ', 'Ẩ', 'Ẫ', 'Ậ',
        'É', 'È', 'Ẻ', 'Ẽ', 'Ẹ', 'Ê', 'Ế', 'Ề', 'Ể', 'Ễ', 'Ệ',
        'Í', 'Ì', 'Ỉ', 'Ĩ', 'Ị',
        'Ó', 'Ò', 'Ỏ', 'Õ', 'Ọ', 'Ô', 'Ơ', 'Ố', 'Ồ', 'Ổ', 'Ỗ', 'Ộ', 'Ớ', 'Ờ', 'Ở', 'Ỡ', 'Ợ',
        'Ú', 'Ù', 'Ủ', 'Ũ', 'Ụ', 'Ư', 'Ứ', 'Ừ', 'Ử', 'Ữ', 'Ự',
        'Ý', 'Ỳ', 'Ỷ', 'Ỹ', 'Ỵ'
    ];

    [GeneratedRegex(@"\{[0-9]+(?::[^}]+)?\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Transforms the input string into a pseudo-localized string:
    /// - Delimited by '[' and ']'
    /// - Expands total character length by approximately 40% (via vowel doubling and trailing padding)
    /// - Replaces ASCII letters with accented glyphs
    /// - Preserves format placeholders like {0}, {1}, {0:d} untouched
    /// </summary>
    public static string Transform(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        // Calculate target expansion count (~40% increase)
        // Delimiters '[' and ']' take 2 characters.
        var targetExpansion = Math.Max(2, (int)Math.Round(input.Length * 0.40, MidpointRounding.AwayFromZero));
        var remainingExpansion = targetExpansion - 2;

        var sb = new StringBuilder(input.Length + targetExpansion + 4);
        sb.Append('[');

        var matches = PlaceholderRegex().Matches(input);
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var textSegment = input[lastIndex..match.Index];
                remainingExpansion = TransformSegment(textSegment, sb, remainingExpansion);
            }

            // Keep format placeholder unchanged
            sb.Append(match.Value);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < input.Length)
        {
            var textSegment = input[lastIndex..];
            remainingExpansion = TransformSegment(textSegment, sb, remainingExpansion);
        }

        // If any target expansion remains (e.g. text had few or no vowels), pad before closing bracket
        while (remainingExpansion > 0)
        {
            sb.Append('~');
            remainingExpansion--;
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static int TransformSegment(string segment, StringBuilder sb, int remainingExpansion)
    {
        foreach (var c in segment)
        {
            var mapped = _charMap.TryGetValue(c, out var mappedChar) ? mappedChar : c;
            sb.Append(mapped);

            if (remainingExpansion > 0 && _vowels.Contains(c))
            {
                sb.Append(mapped);
                remainingExpansion--;
            }
        }

        return remainingExpansion;
    }
}
