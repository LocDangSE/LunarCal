using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace LunarCal.App.UiTests;

/// <summary>
/// UI smoke tests driving LunarCal.exe out-of-process via FlaUI and UI Automation (spec §9, P2-12).
/// Verifies application startup, main window appearance, view switching, and lunar date display.
/// </summary>
[Collection("UiSmokeTests")]
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

    /// <summary>
    /// Launches the app for a test with its own throw-away database and the AI queue worker switched off. A test must
    /// never touch the developer's real profile: an earlier version wrote its "họp chiến lược Q4" command into the real
    /// queue, where the real app later sent it (and a retry loop sent it again and again) to the user's Gemini key until
    /// the daily quota was gone. It also must not kill other LunarCal processes, which may be the developer's own app.
    /// </summary>
    private static Application LaunchApp(string exePath)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "LunarCal_UiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            Arguments = "--skip-first-run",
        };

        psi.Environment["LunarCal__Storage__DatabasePath"] = Path.Combine(dataDirectory, "lunarcal-uitest.db");
        psi.Environment["LunarCal__Ai__QueueWorkerEnabled"] = "false";

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

    private static readonly TimeSpan _uiTimeout = TimeSpan.FromSeconds(10);

    // Figma 01/S06/S07 subtitles: "Âm lịch: tháng 8 – 9 năm Bính Ngọ", "Âm lịch: 3 – 9 tháng 9 …", "Âm lịch: 7 tháng 9 …";
    // English "Lunar: months 8 – 9, …", "Lunar: 3 – 9 of month 9, …", "Lunar: day 7 of month 9, …".
    private static readonly Regex _lunarSubtitle = new(@"^(Âm lịch: (tháng )?\d+|Lunar: (months? |day )?\d+)", RegexOptions.CultureInvariant);

    // Lunar day text in a cell or column header: "7", "1/9", "1/2n" or "1/2L".
    private static readonly Regex _lunarDay = new(@"^\d{1,2}(/\d{1,2}[nL]?)?$", RegexOptions.CultureInvariant);

    private static AutomationElement WaitFor(AutomationElement root, string automationId)
    {
        var element = Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), _uiTimeout).Result;
        element.Should().NotBeNull($"'{automationId}' must appear within {_uiTimeout.TotalSeconds} s");
        return element!;
    }

    private static void WaitGone(AutomationElement root, string automationId)
    {
        var gone = Retry.WhileFalse(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is null, _uiTimeout).Result;
        gone.Should().BeTrue($"'{automationId}' must no longer be shown");
    }

    private static List<string> LunarTexts(AutomationElement root, string automationId) =>
        root.FindAllDescendants(cf => cf.ByAutomationId(automationId))
            .Select(e => e.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    private static void CloseApp(Application app)
    {
        try
        {
            app.Close();
            if (!app.HasExited)
            {
                try { app.Kill(); } catch { }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            app.Kill();
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
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds");
            window.Title.Should().Contain("LunarCal");
            WaitFor(window, "TopBar");

            // The lunar subtitle shows a real lunar month (FR-UI-9, FR-LUNAR-1), not just any text.
            var subtitle = WaitFor(window, "LunarHeaderSubtitleText").Name;
            _lunarSubtitle.IsMatch(subtitle).Should().BeTrue($"subtitle '{subtitle}' must describe a lunar month");

            // Month cells show lunar days (FR-UI-2): 42 labels, all in lunar day format, including at least one mùng 1.
            WaitFor(window, "MonthView");
            Retry.WhileFalse(() => LunarTexts(window, "LunarDayLabel").Count == 42, _uiTimeout);
            var cellTexts = LunarTexts(window, "LunarDayLabel");
            cellTexts.Should().HaveCount(42);
            cellTexts.Should().OnlyContain(t => _lunarDay.IsMatch(t));
            cellTexts.Should().Contain(t => t.Contains('/'), "a 6-week grid always contains a first lunar day");
        }
        finally
        {
            CloseApp(app);
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
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds");

            // 1. Initial state: Month
            var monthRadio = WaitFor(window, "ViewModeMonth").AsRadioButton();
            monthRadio.IsChecked.Should().BeTrue();
            WaitFor(window, "MonthView");

            // 2. Week: the month grid is replaced and every column header has a lunar day (FR-UI-1, FR-UI-2)
            SelectRadioButton(WaitFor(window, "ViewModeWeek").AsRadioButton());
            var weekView = WaitFor(window, "WeekView");
            WaitGone(window, "MonthView");
            var columnLunar = LunarTexts(weekView, "LunarDateText");
            columnLunar.Should().HaveCount(7);
            columnLunar.Should().OnlyContain(t => _lunarDay.IsMatch(t));

            // 3. Day: header shows the lunar label with a lunar day
            SelectRadioButton(WaitFor(window, "ViewModeDay").AsRadioButton());
            var dayView = WaitFor(window, "DayView");
            WaitGone(window, "WeekView");
            LunarTexts(dayView, "LunarDateText").Should().Contain(t => t.Any(char.IsDigit));

            // 4. Agenda
            SelectRadioButton(WaitFor(window, "ViewModeAgenda").AsRadioButton());
            WaitFor(window, "AgendaView");
            WaitGone(window, "DayView");

            // 5. Back to Month
            SelectRadioButton(monthRadio);
            WaitFor(window, "MonthView");
            WaitGone(window, "AgendaView");
        }
        finally
        {
            CloseApp(app);
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
            var settingsButton = WaitFor(window, "BtnSettings").AsButton();
            settingsButton.Should().NotBeNull("the top bar must expose a Settings button (Figma S13)");
            settingsButton!.Invoke();

            var settingsWindow = Retry.WhileNull(
                () => window.ModalWindows.FirstOrDefault(),
                TimeSpan.FromSeconds(5)).Result;
            settingsWindow.Should().NotBeNull("Settings must open as a window owned by the shell");

            // Figma 04: Settings opens on "Chung"; go to "Giao diện" (S13).
            // Select through the UIA SelectionItem pattern, not a mouse click: right after the window opens the
            // radio is in the tree before layout gives it a clickable point (NoClickablePointException).
            var navAppearance = Retry.WhileNull(
                () => settingsWindow!.FindFirstDescendant(cf => cf.ByAutomationId("NavAppearance")), TimeSpan.FromSeconds(5)).Result;
            navAppearance.Should().NotBeNull("the Settings navigation must list the Appearance page");
            SelectRadioButton(navAppearance!.AsRadioButton());

            var page = Retry.WhileNull(
                () => settingsWindow!.FindFirstDescendant(cf => cf.ByAutomationId("AppearanceSettingsPage")),
                TimeSpan.FromSeconds(5)).Result;
            page.Should().NotBeNull("the Appearance page (S13) must be hosted in the Settings window");

            // FR-UI-7, FR-UI-8: switching theme at runtime must not crash or close the app.
            foreach (var id in new[] { "ThemeRadioDark", "ThemeRadioLight", "ThemeRadioSystem" })
            {
                var radio = settingsWindow!.FindFirstDescendant(cf => cf.ByAutomationId(id))?.AsRadioButton();
                radio.Should().NotBeNull($"theme option {id} must exist");
                SelectRadioButton(radio);
                Retry.WhileFalse(() => radio!.IsChecked == true, TimeSpan.FromSeconds(2));
                radio!.IsChecked.Should().BeTrue($"{id} must become the selected theme");
                app.HasExited.Should().BeFalse($"the app must survive switching to {id}");
            }

            settingsWindow!.Close();
        }
        finally
        {
            CloseApp(app);
        }
    }

    [Fact]
    public void Ai_command_bar_and_queue_drawer_smoke_test()
    {
        var exePath = FindAppPath();
        using var automation = new UIA3Automation();
        using var app = LaunchApp(exePath);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds");

            // FR-UI-5: AI command bar input and submit button exist
            var aiInput = WaitFor(window, "AiCommandInput");
            aiInput.Should().NotBeNull("the shell must host the AI command input");

            var btnSubmit = WaitFor(window, "BtnAiSubmit");
            btnSubmit.Should().NotBeNull("the shell must host the AI submit button");

            // FR-QUEUE-2: Clicking sync status block opens the AI Queue Drawer
            var syncBlock = WaitFor(window, "SyncStatusBlock").AsButton();
            syncBlock.Should().NotBeNull();
            syncBlock!.Invoke();

            var drawer = WaitFor(window, "AiQueueDrawer");
            drawer.Should().NotBeNull("clicking sync status block must open the AI Queue Drawer");

            var btnCloseDrawer = WaitFor(drawer, "BtnCloseAiQueueDrawer").AsButton();
            btnCloseDrawer.Should().NotBeNull();
            btnCloseDrawer!.Invoke();

            WaitGone(window, "AiQueueDrawer");
        }
        finally
        {
            CloseApp(app);
        }
    }

    [Fact]
    public void Ai_confirm_dialog_smoke_test()
    {
        var exePath = FindAppPath();
        using var automation = new UIA3Automation();
        using var app = LaunchApp(exePath);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Main window did not appear within 10 seconds");

            // 1. Enter an AI command in the command bar
            var aiInput = WaitFor(window, "AiCommandInput").AsTextBox();
            aiInput.Should().NotBeNull();
            aiInput!.Text = "họp chiến lược Q4";

            var btnSubmit = WaitFor(window, "BtnAiSubmit").AsButton();
            btnSubmit.Should().NotBeNull();
            btnSubmit!.Invoke();

            // 2. Open the AI Queue Drawer from sidebar sync status
            var syncBlock = WaitFor(window, "SyncStatusBlock").AsButton();
            syncBlock.Should().NotBeNull();
            syncBlock!.Invoke();

            var drawer = WaitFor(window, "AiQueueDrawer");
            drawer.Should().NotBeNull();

            // 3. Find the action button to confirm the command
            var btnConfirmAction = Retry.WhileNull(
                () => drawer.FindFirstDescendant(cf => cf.ByAutomationId("BtnActionConfirm"))
                      ?? drawer.FindFirstDescendant(cf => cf.ByAutomationId("BtnActionView")),
                TimeSpan.FromSeconds(8))?.Result;
            btnConfirmAction.Should().NotBeNull("the queued command should have a confirm or view action button");
            btnConfirmAction!.AsButton()!.Invoke();

            // 4. Modal AiConfirmDialog (Figma 02 2:2 / 2:494) must appear
            var modalDialog = Retry.WhileNull(
                () => window.ModalWindows.FirstOrDefault(w => w.AutomationId == "AiConfirmDialog")
                      ?? app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.AutomationId == "AiConfirmDialog"),
                TimeSpan.FromSeconds(8))?.Result;

            modalDialog.Should().NotBeNull("AiConfirmDialog must appear when user clicks confirm/view");

            // Verify elements of Figma 02
            var title = WaitFor(modalDialog!, "AiConfirmTitle");
            title.Should().NotBeNull();

            var statusBadge = WaitFor(modalDialog!, "AiConfirmStatusBadge");
            statusBadge.Should().NotBeNull();

            var originalText = WaitFor(modalDialog!, "AiConfirmOriginalText");
            originalText.Should().NotBeNull();

            var btnCancel = WaitFor(modalDialog!, "BtnAiConfirmCancel").AsButton();
            btnCancel.Should().NotBeNull();
            btnCancel!.Invoke();

            // Verify dialog closes cleanly
            Retry.WhileFalse(() => modalDialog!.IsOffscreen, TimeSpan.FromSeconds(5));
        }
        finally
        {
            CloseApp(app);
        }
    }
}
