using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using LunarCal.App.Controls;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// P4-R7: the progress card must really load with the app resources (a missing resource only fails when the XAML is
/// loaded) and show the right buttons for each state.
/// </summary>
[Collection(nameof(EventEditorDialogTests))]
public sealed class AiProgressCardRenderTests
{
    private sealed record Outcome(Exception? Error, bool CardVisible, string? Title, bool CancelVisible, bool ViewVisible, bool OpenQueueVisible, string? Background = null);

    [Fact]
    public void FR_QUEUE_2_The_card_loads_and_shows_Cancel_while_the_model_works()
    {
        var result = OnSta((vm, _) => vm.Track(Command()));

        result.Error.Should().BeNull(result.Error?.ToString());
        result.CardVisible.Should().BeTrue();
        result.Title.Should().Contain("#1019");
        result.CancelVisible.Should().BeTrue();
        result.ViewVisible.Should().BeFalse();
        result.OpenQueueVisible.Should().BeFalse();
    }

    [Fact]
    public void FR_QUEUE_2_The_card_is_hidden_when_nothing_is_followed()
    {
        var result = OnSta((_, _) => { });

        result.Error.Should().BeNull(result.Error?.ToString());
        result.CardVisible.Should().BeFalse();
    }

    [Fact]
    public void FR_CHAT_7_A_failed_command_is_drawn_with_the_danger_colours_of_Figma_4_5()
    {
        // Regression: the colours were local attributes, which beat the style trigger, so Failed looked like Processing.
        var result = OnSta((vm, store) =>
        {
            vm.Track(Command());
            store.GetByIdAsync("c1", Arg.Any<CancellationToken>()).Returns(Command() with { Status = AiCommandStatus.Error, RetryCount = 0, NextRetryAt = null, LastError = "boom" });
            vm.TickAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        result.Error.Should().BeNull(result.Error?.ToString());
        result.OpenQueueVisible.Should().BeTrue();
        result.CancelVisible.Should().BeFalse();
        result.Background.Should().Be("#FFFEF3F2", "DangerSubtleBrush");
    }

    private static AiCommand Command() => new()
    {
        Id = "c1",
        DisplayNo = 1019,
        Text = "nhắc tôi họp 6h tối mai",
        TypedAt = DateTimeOffset.Now,
        TimeZone = "SE Asia Standard Time",
        InputLang = "vi",
        Status = AiCommandStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Outcome OnSta(Action<AiProgressViewModel, IAiCommandStore> arrange)
    {
        Outcome? outcome = null;
        Application? app = null;
        var thread = new Thread(() =>
        {
            try
            {
                app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Tokens.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Themes/Light.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Controls.xaml", UriKind.Relative) });

                var store = Substitute.For<IAiCommandStore>();
                var vm = new AiProgressViewModel(store, new LocalizationService(SupportedCultures.Vietnamese));
                arrange(vm, store);
                var card = new AiProgressCard { DataContext = vm };
                card.Measure(new Size(900, 400));
                card.Arrange(new Rect(new Size(900, 400)));
                card.UpdateLayout();

                var buttons = Descendants<Button>(card).ToList();
                bool Visible(string id) => buttons.Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == id).Visibility == Visibility.Visible;
                var title = Descendants<TextBlock>(card).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "AiProgressTitle").Text;
                var border = Descendants<Border>(card).First(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "AiProgressCard");
                outcome = new Outcome(null, border.Visibility == Visibility.Visible, title, Visible("BtnAiProgressCancel"), Visible("BtnAiProgressView"), Visible("BtnAiProgressOpenQueue"), (border.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                outcome = new Outcome(ex, false, null, false, false, false);
            }
            finally
            {
                app?.Resources.MergedDictionaries.Clear();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return outcome!;
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
