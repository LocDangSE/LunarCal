using AwesomeAssertions;
using LunarCal.App.Services;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.Themes;

public sealed class ThemeServiceTests
{
    [Fact]
    public void FR_UI_7_Theme_follows_system_setting_when_system_is_dark()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsDarkTheme.Returns(true);
        detector.IsHighContrast.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("System");

        service.CurrentThemeSetting.Should().Be("System");
        service.ActiveTheme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void FR_UI_7_Theme_follows_system_setting_when_system_is_light()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsDarkTheme.Returns(false);
        detector.IsHighContrast.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("System");

        service.CurrentThemeSetting.Should().Be("System");
        service.ActiveTheme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void FR_UI_7_Explicit_dark_setting_overrides_light_system_mode()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsDarkTheme.Returns(false);
        detector.IsHighContrast.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("Dark");

        service.CurrentThemeSetting.Should().Be("Dark");
        service.ActiveTheme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void FR_UI_7_Explicit_light_setting_overrides_dark_system_mode()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsDarkTheme.Returns(true);
        detector.IsHighContrast.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("Light");

        service.CurrentThemeSetting.Should().Be("Light");
        service.ActiveTheme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void FR_UI_8_High_contrast_mode_takes_precedence_for_accessibility()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsHighContrast.Returns(true);
        detector.IsDarkTheme.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("Light");

        service.IsHighContrast.Should().BeTrue();
        service.ActiveTheme.Should().Be(AppTheme.HighContrast);
    }

    [Fact]
    public void FR_UI_7_ThemeChanged_event_fires_when_active_theme_changes()
    {
        var detector = Substitute.For<ISystemThemeDetector>();
        detector.IsDarkTheme.Returns(false);
        detector.IsHighContrast.Returns(false);

        using var service = new ThemeService(detector);
        service.ApplyTheme("Light");

        AppTheme? notifiedTheme = null;
        service.ThemeChanged += (_, theme) => notifiedTheme = theme;

        service.ApplyTheme("Dark");

        notifiedTheme.Should().Be(AppTheme.Dark);
        service.ActiveTheme.Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData(AppTheme.Light, "Themes/Light.xaml")]
    [InlineData(AppTheme.Dark, "Themes/Dark.xaml")]
    [InlineData(AppTheme.HighContrast, "Themes/HighContrast.xaml")]
    public void FR_UI_7_GetThemeUri_returns_correct_pack_uri(AppTheme theme, string expectedFragment)
    {
        var uri = ThemeService.GetThemeUri(theme);
        uri.OriginalString.Should().Contain(expectedFragment);
        uri.Scheme.Should().Be("pack");
    }
}
