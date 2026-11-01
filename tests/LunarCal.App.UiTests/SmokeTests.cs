using System.Diagnostics;
using AwesomeAssertions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace LunarCal.App.UiTests;

/// <summary>
/// UI smoke tests driving LunarCal.exe out-of-process via FlaUI and UI Automation (spec §9, P2-12).
/// Verifies application startup, main window appearance, view switching, and lunar date display.
/// </summary>
public sealed class SmokeTests
{
    private static string FindAppPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // Use the app build with the same configuration as this test assembly, so a stale build of the other
        // configuration is never launched (and coverage reports do not mix Debug and Release sequence points).
        var config = baseDir.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var appDir = Path.GetFullPath(Path.Combine(baseDir, $"../../../../../src/LunarCal.App/bin/{config}/net10.0-windows10.0.19041.0"));
        var exePath = Path.Combine(appDir, "LunarCal.exe");
        var dllPath = Path.Combine(appDir, "LunarCal.dll");
        if (File.Exists(exePath) && File.Exists(dllPath))
        {
            return exePath;
        }

        throw new FileNotFoundException($"LunarCal.exe and LunarCal.dll ({config}) not found in {appDir}");
    }

    private static Application LaunchApp(string exePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        return Application.Launch(psi);
    }

    private static void SelectRadioButton(RadioButton? radio)
    {
        ArgumentNullException.ThrowIfNull(radio);
        if (radio.Patterns.SelectionItem.TryGetPattern(out var pattern))
        {
            pattern.Select();
        }
        else
        {
            radio.IsChecked = true;
        }
    }

    [Fact]
    public void App_starts_and_shows_main_window()
    {
        var exePath = FindAppPath();
        using var automation = new UIA3Automation();
        using var app = LaunchApp(exePath);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            window.Should().NotBeNull("Main window must appear within 10 seconds");
            window.Title.Should().Contain("LunarCal");

            // Verify TopBar exists
            var topBar = window.FindFirstDescendant(cf => cf.ByAutomationId("TopBar"));
            topBar.Should().NotBeNull("TopBar must be present in the main shell");

            // Verify Lunar header subtitle is displayed with lunar text
            var lunarSubtitle = window.FindFirstDescendant(cf => cf.ByAutomationId("LunarHeaderSubtitleText"));
            lunarSubtitle.Should().NotBeNull("Lunar header subtitle must be present");
            lunarSubtitle.Name.Should().NotBeNullOrWhiteSpace("Lunar header subtitle must contain lunar date information");

            // Verify default view is MonthView
            var monthView = window.FindFirstDescendant(cf => cf.ByAutomationId("MonthView"));
            monthView.Should().NotBeNull("MonthView should be visible by default");
        }
        finally
        {
            try
            {
                app.Close();
            }
            catch
            {
                app.Kill();
            }
        }
    }

    [Fact]
    public void View_switcher_transitions_between_calendar_views()
    {
        var exePath = FindAppPath();
        using var automation = new UIA3Automation();
        using var app = LaunchApp(exePath);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
            window.Should().NotBeNull("Main window must appear within 10 seconds");

            // 1. Initial state: MonthView visible
            var monthRadio = window.FindFirstDescendant(cf => cf.ByAutomationId("ViewModeMonth"))?.AsRadioButton();
            monthRadio.Should().NotBeNull("Month radio button should exist in view switcher");
            monthRadio!.IsChecked.Should().BeTrue();

            var monthView = window.FindFirstDescendant(cf => cf.ByAutomationId("MonthView"));
            monthView.Should().NotBeNull("MonthView must be present initially");

            // 2. Switch to Week View
            var weekRadio = window.FindFirstDescendant(cf => cf.ByAutomationId("ViewModeWeek"))?.AsRadioButton();
            weekRadio.Should().NotBeNull("Week radio button should exist in view switcher");
            SelectRadioButton(weekRadio);
            Thread.Sleep(500);

            var weekView = window.FindFirstDescendant(cf => cf.ByAutomationId("WeekView"));
            weekView.Should().NotBeNull("WeekView must be displayed when Week mode is selected");

            // 3. Switch to Day View
            var dayRadio = window.FindFirstDescendant(cf => cf.ByAutomationId("ViewModeDay"))?.AsRadioButton();
            dayRadio.Should().NotBeNull("Day radio button should exist in view switcher");
            SelectRadioButton(dayRadio);
            Thread.Sleep(500);

            var dayView = window.FindFirstDescendant(cf => cf.ByAutomationId("DayView"));
            dayView.Should().NotBeNull("DayView must be displayed when Day mode is selected");

            // 4. Switch to Agenda View
            var agendaRadio = window.FindFirstDescendant(cf => cf.ByAutomationId("ViewModeAgenda"))?.AsRadioButton();
            agendaRadio.Should().NotBeNull("Agenda radio button should exist in view switcher");
            SelectRadioButton(agendaRadio);
            Thread.Sleep(500);

            var agendaView = window.FindFirstDescendant(cf => cf.ByAutomationId("AgendaView"));
            agendaView.Should().NotBeNull("AgendaView must be displayed when Agenda mode is selected");

            // 5. Switch back to Month View
            SelectRadioButton(monthRadio);
            Thread.Sleep(500);
            monthView = window.FindFirstDescendant(cf => cf.ByAutomationId("MonthView"));
            monthView.Should().NotBeNull("MonthView must be displayed when returning to Month mode");
        }
        finally
        {
            try
            {
                app.Close();
            }
            catch
            {
                app.Kill();
            }
        }
    }

    [Fact]
    public void Settings_button_opens_appearance_page()
    {
        var exePath = FindAppPath();
        using var automation = new UIA3Automation();
        using var app = LaunchApp(exePath);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds");
            var settingsButton = window.FindFirstDescendant(cf => cf.ByAutomationId("BtnSettings"))?.AsButton();
            settingsButton.Should().NotBeNull("the top bar must expose a Settings button (Figma S13)");
            settingsButton!.Invoke();

            var settingsWindow = FlaUI.Core.Tools.Retry.WhileNull(
                () => window.ModalWindows.FirstOrDefault(),
                TimeSpan.FromSeconds(5)).Result;
            settingsWindow.Should().NotBeNull("Settings must open as a window owned by the shell");

            var page = FlaUI.Core.Tools.Retry.WhileNull(
                () => settingsWindow!.FindFirstDescendant(cf => cf.ByAutomationId("AppearanceSettingsPage")),
                TimeSpan.FromSeconds(5)).Result;
            page.Should().NotBeNull("the Appearance page (S13) must be hosted in the Settings window");

            // FR-UI-7, FR-UI-8: switching theme at runtime must not crash or close the app.
            foreach (var id in new[] { "ThemeRadioDark", "ThemeRadioHighContrast", "ThemeRadioLight", "ThemeRadioSystem" })
            {
                var radio = settingsWindow!.FindFirstDescendant(cf => cf.ByAutomationId(id))?.AsRadioButton();
                radio.Should().NotBeNull($"theme option {id} must exist");
                SelectRadioButton(radio);
                FlaUI.Core.Tools.Retry.WhileFalse(() => radio!.IsChecked == true, TimeSpan.FromSeconds(2));
                radio!.IsChecked.Should().BeTrue($"{id} must become the selected theme");
                app.HasExited.Should().BeFalse($"the app must survive switching to {id}");
            }

            settingsWindow!.Close();
        }
        finally
        {
            try
            {
                app.Close();
            }
            catch
            {
                app.Kill();
            }
        }
    }
}
