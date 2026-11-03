using System.Windows;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Infrastructure.Storage;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// FR-UI-4 (P3-REVIEW): the event editor dialog (Figma S10) must be constructible with the app resources. Before the
/// review fix, the XAML referenced converters that did not exist and the dialog threw a XamlParseException on open.
/// </summary>
public sealed class EventEditorDialogTests
{
    [Fact]
    public void FR_UI_4_Event_editor_dialog_opens_and_binds_start_date_and_time()
    {
        string? title = null;
        DateTime? startPicker = null;
        string? startTimeText = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Tokens.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Themes/Light.xaml", UriKind.Relative) });

                var vm = new EventEditorViewModel(
                    new InMemoryEventStore(seedSampleData: false),
                    new LocalizationService(SupportedCultures.Vietnamese),
                    new TestDisplayPreferences());
                vm.Initialize(new EventEditorRequest(EventEditorMode.Create, Date: new DateOnly(2026, 10, 22), Time: new TimeOnly(10, 0)));

                var dialog = new EventEditorDialog(vm);
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(560, 900));
                content.Arrange(new Rect(new Size(560, 900)));
                content.UpdateLayout();

                title = dialog.Title;
                startPicker = FindByAutomationId<System.Windows.Controls.DatePicker>(content, "DpStartDate").SelectedDate;
                startTimeText = FindByAutomationId<System.Windows.Controls.TextBox>(content, "TxtStartTime").Text;
                dialog.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        error.Should().BeNull(error?.ToString());
        title.Should().NotBeNullOrWhiteSpace();
        startPicker.Should().Be(new DateTime(2026, 10, 22));
        startTimeText.Should().Be("10:00");
    }

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match && System.Windows.Automation.AutomationProperties.GetAutomationId(child) == automationId)
            {
                return match;
            }

            var nested = TryFind<T>(child, automationId);
            if (nested is not null)
            {
                return nested;
            }
        }

        throw new InvalidOperationException($"Element '{automationId}' not found.");
    }

    private static T? TryFind<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        try
        {
            return FindByAutomationId<T>(root, automationId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
