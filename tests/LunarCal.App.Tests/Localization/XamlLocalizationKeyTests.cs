using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using LunarCal.App.Localization;
using Xunit;

namespace LunarCal.App.Tests.Localization;

/// <summary>
/// FR-I18N-3 (P3-REVIEW): a <c>{loc:Loc key}</c> in XAML whose key is not in the resx renders as "!key!" at runtime
/// (the event editor shipped with four such keys). Scans every XAML file of the app against both languages.
/// </summary>
public sealed partial class XamlLocalizationKeyTests
{
    [GeneratedRegex(@"\{loc:Loc\s+(?:Key=)?([A-Za-z0-9_.]+)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex LocKey();

    [Fact]
    public void FR_I18N_3_Every_Loc_key_used_in_xaml_exists_in_vi_and_en_resx()
    {
        var appDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/LunarCal.App"));
        Directory.Exists(appDir).Should().BeTrue($"the app sources must be at {appDir}");

        var keys = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => LocKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Distinct()
            .ToList();
        keys.Should().NotBeEmpty();

        var vi = new LocalizationService(SupportedCultures.Vietnamese);
        var en = new LocalizationService(SupportedCultures.English);
        var missing = keys
            .Where(k => vi[k.Key].StartsWith('!') || en[k.Key].StartsWith('!'))
            .Select(k => $"{k.File}: {k.Key}")
            .ToList();

        missing.Should().BeEmpty("every XAML localization key needs a vi-VN and en-US value");
    }
}
