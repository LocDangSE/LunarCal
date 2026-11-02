using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// Regression tests for visual bugs found by comparing the running app with Figma (P2-R10): invisible text on
/// accent fills, pills drawn as ellipses, overlapping lunar text in the day panel, collapsed event blocks and
/// emoji glyphs used as icons.
/// </summary>
public sealed class UiRegressionTests
{
    private const string AccentFill = "{DynamicResource AccentPrimaryBrush}";
    private const string AccentText = "{DynamicResource AccentTextBrush}";
    private const string FullRadius = "{DynamicResource CornerRadiusFull}";

    private static readonly string _appDirectory = FindAppDirectory();
    private static readonly string[] _viewDirectories = ["Views", "Controls"];
    private static readonly string[] _textAttributes = ["Text", "Content"];

    [Fact]
    public void FR_UI_8_Text_on_accent_fill_uses_on_accent_brush()
    {
        // AccentTextBrush is #1D4ED8 (blue text for subtle chips); on an AccentPrimaryBrush fill it is invisible.
        var violations = new List<string>();
        foreach (var (file, doc) in LoadViewXaml())
        {
            foreach (var element in doc.Descendants().Where(e => Attr(e, "Background") == AccentFill))
            {
                violations.AddRange(element.Descendants()
                    .Where(d => Attr(d, "Foreground") == AccentText || Attr(d, "Stroke") == AccentText)
                    .Select(d => $"{file}:{Line(d)}"));
            }

            // Style setters and trigger setters that put accent text on an accent fill.
            foreach (var group in doc.Descendants().Where(e => e.Name.LocalName is "Style" or "Trigger" or "DataTrigger" or "MultiDataTrigger" or "MultiTrigger"))
            {
                var setters = group.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                if (setters.Any(s => Attr(s, "Property") == "Background" && Attr(s, "Value") == AccentFill))
                {
                    violations.AddRange(setters
                        .Where(s => Attr(s, "Value") == AccentText)
                        .Select(s => $"{file}:{Line(s)}"));
                }
            }
        }

        violations.Should().BeEmpty("text on an accent fill must use OnAccentBrush");
    }

    [Fact]
    public void FR_UI_11_Full_corner_radius_is_only_used_on_square_elements()
    {
        // WPF does not clamp CornerRadius like CSS: 999 on a 280×44 button scales the radii into an ellipse.
        var violations = new List<string>();
        foreach (var (file, doc) in LoadViewXaml())
        {
            foreach (var element in doc.Descendants().Where(e => Attr(e, "CornerRadius") == FullRadius))
            {
                var (width, height) = SizeOf(element);
                if (width is null || width != height)
                {
                    violations.Add($"{file}:{Line(element)} ({width ?? "?"}×{height ?? "?"})");
                }
            }
        }

        violations.Should().BeEmpty("CornerRadiusFull is for circles; pills use CornerRadiusPill");
    }

    [Fact]
    public void FR_LUNAR_6_Boolean_to_visibility_bindings_do_not_rely_on_converter_parameter()
    {
        // The built-in BooleanToVisibilityConverter ignores ConverterParameter, so "Inverse" showed the
        // "lunar calendar is off" text on top of the lunar block in the day panel.
        var violations = LoadViewXaml()
            .SelectMany(x => x.Doc.Descendants().SelectMany(e => e.Attributes()
                .Where(a => a.Value.Contains("StaticResource BoolToVis", StringComparison.Ordinal)
                    && a.Value.Contains("ConverterParameter", StringComparison.Ordinal))
                .Select(_ => $"{x.File}:{Line(e)}")))
            .ToList();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FR_UI_11_Icons_do_not_use_emoji_or_symbol_characters()
    {
        // Emoji (🌙 📅 ✨ ⚙) and arrows (↑ ↻ ✕ < >) render differently from the Figma line icons and depend on
        // the installed fonts; icons come from the IconGlyph* tokens (Segoe Fluent Icons) or geometry resources.
        var violations = new List<string>();
        foreach (var (file, doc) in LoadViewXaml())
        {
            foreach (var element in doc.Descendants())
            {
                foreach (var name in _textAttributes)
                {
                    var value = Attr(element, name);
                    if (value is not null && !value.StartsWith('{') && value.Any(IsSymbol))
                    {
                        violations.Add($"{file}:{Line(element)} {name}=\"{value}\"");
                    }
                }
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FR_UI_1_Week_view_timed_event_fills_its_day_column()
    {
        var (columnWidth, chipWidth) = RunOnSta(() =>
        {
            var start = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(7));
            var store = Substitute.For<IEventStore>();
            store.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(
                [
                    new CalendarEvent
                    {
                        Id = "e1",
                        CalendarId = "cal",
                        Title = "Họp nhóm đồ án",
                        StartUtc = start.ToUniversalTime(),
                        EndUtc = start.AddHours(1).ToUniversalTime(),
                    },
                ]));

            using var vm = new WeekViewModel(store, new LocalizationService(SupportedCultures.Vietnamese), new TestDisplayPreferences());
            vm.SetDisplayDateAsync(new DateOnly(2026, 10, 12)).GetAwaiter().GetResult();
            var view = new WeekView { DataContext = vm };
            view.Resources.MergedDictionaries.Add(LoadAppResources());
            var size = new Size(1100, 760);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            var chip = FindAll<Border>(view).Single(b => AutomationProperties.GetAutomationId(b) == "TimedEventChip");
            var column = FindAncestor<ContentPresenter>(FindAncestor<ItemsControl>(chip)!)!;
            return (column.ActualWidth, chip.ActualWidth);
        });

        columnWidth.Should().BeGreaterThan(100);
        chipWidth.Should().BeGreaterThan(columnWidth * 0.8, "a single event takes the whole column, not a 20 px sliver");
    }

    [Theory]
    [InlineData("Month")]
    [InlineData("Week")]
    [InlineData("Day")]
    [InlineData("Agenda")]
    public void FR_UI_1_Calendar_views_load_and_lay_out_with_app_resources(string view)
    {
        // A XAML error (wrong resource type, duplicate property) only shows up when the view is loaded; the
        // P2-R11 shell crashed at start-up on a double used as a GridLength.
        var size = RunOnSta(() =>
        {
            var store = Substitute.For<IEventStore>();
            store.GetEventsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CalendarEvent>>(Array.Empty<CalendarEvent>()));
            store.GetCalendarsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<CalendarInfo>>(Array.Empty<CalendarInfo>()));
            var loc = new LocalizationService(SupportedCultures.Vietnamese);
            var prefs = new TestDisplayPreferences();
            FrameworkElement element = view switch
            {
                "Month" => new MonthView { DataContext = new MonthViewModel(store, loc, prefs) },
                "Week" => new WeekView { DataContext = new WeekViewModel(store, loc, prefs) },
                "Day" => new DayView { DataContext = new DayViewModel(store, loc, prefs) },
                _ => new AgendaView { DataContext = new AgendaViewModel(store, loc, prefs) },
            };
            element.Resources.MergedDictionaries.Add(LoadAppResources());
            var bounds = new Size(1160, 744);
            element.Measure(bounds);
            element.Arrange(new Rect(bounds));
            element.UpdateLayout();
            return element.RenderSize;
        });

        size.Width.Should().Be(1160);
    }

    private static bool IsSymbol(char c) =>
        char.IsSurrogate(c)
        || c is '<' or '>'
        || (c >= '←' && c <= '⯿'); // arrows, math, technical, dingbats, misc symbols

    private static (string? Width, string? Height) SizeOf(XElement element)
    {
        var width = Attr(element, "Width");
        var height = Attr(element, "Height");
        if (width is not null || height is not null)
        {
            return (width, height);
        }

        // Template parts take their size from the Width/Height setters of the enclosing style.
        var style = element.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Style");
        var setters = style?.Elements().Where(e => e.Name.LocalName == "Setter").ToList() ?? [];
        return (
            setters.FirstOrDefault(s => Attr(s, "Property") == "Width") is { } w ? Attr(w, "Value") : null,
            setters.FirstOrDefault(s => Attr(s, "Property") == "Height") is { } h ? Attr(h, "Value") : null);
    }

    private static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    private static int Line(XElement element) => ((System.Xml.IXmlLineInfo)element).LineNumber;

    private static IEnumerable<(string File, XDocument Doc)> LoadViewXaml() =>
        _viewDirectories
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(_appDirectory, d), "*.xaml", SearchOption.AllDirectories))
            .Select(f => (Path.GetRelativePath(_appDirectory, f), XDocument.Load(f, LoadOptions.SetLineInfo)));

    private static IEnumerable<T> FindAll<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(start); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static ResourceDictionary LoadAppResources()
    {
        var merged = new ResourceDictionary();
        foreach (var relative in new[] { Path.Combine("Resources", "Tokens.xaml"), Path.Combine("Themes", "Light.xaml") })
        {
            using var stream = File.OpenRead(Path.Combine(_appDirectory, relative));
            merged.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }

        return merged;
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

            current = Path.GetDirectoryName(current);
        }

        throw new DirectoryNotFoundException("src/LunarCal.App not found");
    }

    private static T RunOnSta<T>(Func<T> work)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return error is null ? result! : throw new InvalidOperationException("STA work failed", error);
    }
}
