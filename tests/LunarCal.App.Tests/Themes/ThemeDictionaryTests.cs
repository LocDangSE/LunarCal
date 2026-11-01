using System.IO;
using System.Windows;
using System.Windows.Markup;
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
            "SidebarBackgroundBrush",
            "BorderBrush",
            "TextPrimaryBrush",
            "TextSecondaryBrush",
            "AccentPrimaryBrush",
            "AccentHoverBrush",
            "TodayHighlightBrush",
            "TodayBackgroundBrush",
            "LunarTextPrimaryBrush",
            "LunarTextSecondaryBrush",
            "DangerBrush",
            "SuccessBrush",
            "WarningBrush",
            "HoverBackgroundBrush",
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
