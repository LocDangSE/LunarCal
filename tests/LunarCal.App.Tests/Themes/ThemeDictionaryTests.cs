using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AwesomeAssertions;
using Xunit;

namespace LunarCal.App.Tests.Themes;

public sealed class ThemeDictionaryTests
{
    private static readonly string _appDirectory = FindAppDirectory();

    [Fact]
    public void FR_UI_7_Theme_dictionaries_have_identical_resource_keys()
    {
        var lightPath = Path.Combine(_appDirectory, "Themes", "Light.xaml");
        var darkPath = Path.Combine(_appDirectory, "Themes", "Dark.xaml");
        var hcPath = Path.Combine(_appDirectory, "Themes", "HighContrast.xaml");

        File.Exists(lightPath).Should().BeTrue();
        File.Exists(darkPath).Should().BeTrue();
        File.Exists(hcPath).Should().BeTrue();

        var lightDict = LoadDictionary(lightPath);
        var darkDict = LoadDictionary(darkPath);
        var hcDict = LoadDictionary(hcPath);

        var lightKeys = lightDict.Keys.Cast<object>().Select(k => k.ToString()).ToHashSet();
        var darkKeys = darkDict.Keys.Cast<object>().Select(k => k.ToString()).ToHashSet();
        var hcKeys = hcDict.Keys.Cast<object>().Select(k => k.ToString()).ToHashSet();

        lightKeys.Should().NotBeEmpty();

        // Dark theme must contain every key from Light theme
        darkKeys.Should().BeEquivalentTo(lightKeys);

        // High Contrast theme must contain every key from Light theme
        hcKeys.Should().BeEquivalentTo(lightKeys);
    }

    [Fact]
    public void FR_UI_7_Theme_dictionaries_contain_all_required_spec_brushes()
    {
        var lightPath = Path.Combine(_appDirectory, "Themes", "Light.xaml");
        var lightDict = LoadDictionary(lightPath);

        var requiredBrushes = new[]
        {
            "WindowBackgroundBrush",
            "SurfaceBackgroundBrush",
            "SurfaceMutedBrush",
            "SidebarBackgroundBrush",
            "ControlTrackBrush",
            "BorderBrush",
            "BorderSubtleBrush",
            "BorderStrongBrush",
            "TextPrimaryBrush",
            "TextBodyBrush",
            "TextSecondaryBrush",
            "TextTertiaryBrush",
            "TextDisabledBrush",
            "AccentPrimaryBrush",
            "AccentHoverBrush",
            "AccentSubtleBrush",
            "AccentTextBrush",
            "OnAccentBrush",
            "TodayHighlightBrush",
            "TodayBackgroundBrush",
            "SelectedDayBackgroundBrush",
            "LunarTextPrimaryBrush",
            "LunarTextSecondaryBrush",
            "LunarSubtleBrush",
            "LunarStrongBrush",
            "AiAccentBrush",
            "AiSubtleBrush",
            "AiTextBrush",
            "DangerBrush",
            "DangerSubtleBrush",
            "SuccessBrush",
            "SuccessTextBrush",
            "SuccessSubtleBrush",
            "WarningBrush",
            "WarningTextBrush",
            "WarningSubtleBrush",
            "HoverBackgroundBrush",
            "NowLineBrush",
            "ScrimBrush",
        };

        foreach (var brushKey in requiredBrushes)
        {
            lightDict.Contains(brushKey).Should().BeTrue($"Resource '{brushKey}' must be defined in theme dictionary");
        }
    }

    [Fact]
    public void FR_UI_7_Tokens_dictionary_contains_typography_and_layout_tokens()
    {
        var tokensPath = Path.Combine(_appDirectory, "Resources", "Tokens.xaml");
        File.Exists(tokensPath).Should().BeTrue();

        var tokensDict = LoadDictionary(tokensPath);

        tokensDict.Contains("FontFamilyBase").Should().BeTrue();
        tokensDict.Contains("FontSizeDisplay").Should().BeTrue();
        tokensDict.Contains("FontSizeTitle").Should().BeTrue();
        tokensDict.Contains("FontSizeBody").Should().BeTrue();
        tokensDict.Contains("SpacingMd").Should().BeTrue();
        tokensDict.Contains("CornerRadiusMedium").Should().BeTrue();
        tokensDict.Contains("TopBarHeight").Should().BeTrue();
        tokensDict.Contains("SidebarWidth").Should().BeTrue();
        tokensDict.Contains("AiCommandBarHeight").Should().BeTrue();
    }

    [Fact]
    public void FR_UI_11_Light_theme_matches_figma_palette_and_metrics()
    {
        var lightPath = Path.Combine(_appDirectory, "Themes", "Light.xaml");
        var lightDict = LoadDictionary(lightPath);
        var tokensPath = Path.Combine(_appDirectory, "Resources", "Tokens.xaml");
        var tokensDict = LoadDictionary(tokensPath);

        // Figma accent blue #2F6BFF
        ((SolidColorBrush)lightDict["AccentPrimaryBrush"]).Color.Should().Be(Color.FromRgb(0x2F, 0x6B, 0xFF));
        // Figma lunar orange/brown #B54708
        ((SolidColorBrush)lightDict["LunarTextPrimaryBrush"]).Color.Should().Be(Color.FromRgb(0xB5, 0x47, 0x08));
        // Figma window background #F6F7F9
        ((SolidColorBrush)lightDict["WindowBackgroundBrush"]).Color.Should().Be(Color.FromRgb(0xF6, 0xF7, 0xF9));

        // Layout metrics from Figma
        ((double)tokensDict["TopBarHeight"]).Should().Be(64);
        ((double)tokensDict["SidebarWidth"]).Should().Be(280);
        ((double)tokensDict["AiCommandBarHeight"]).Should().Be(92);
    }

    private static ResourceDictionary LoadDictionary(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static string FindAppDirectory()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "src", "LunarCal.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate src/LunarCal.App directory from " + AppContext.BaseDirectory);
    }
}
