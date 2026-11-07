using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// FR-AUTH-5 (ADR-0029, Figma S14 1.5): the account switch dialog must really open with the app resources, because a
/// missing StaticResource only fails when the XAML is loaded (the same class of bug that once broke the S10 dialog).
/// </summary>
[Collection(nameof(EventEditorDialogTests))]
public sealed class AccountSwitchDialogTests
{
    private static readonly AccountSignInAssessment _assessment = new()
    {
        Kind = AccountSignInKind.DifferentAccount,
        CurrentAccount = new AccountInfo { Id = "old-1", Email = "loc.dang@gmail.com" },
        IncomingAccount = new AccountInfo { Id = "new-2", Email = "an.nguyen@gmail.com" },
        PendingChanges = 3,
        PendingAiCommands = 2,
        LocalLunarRules = 4,
    };

    [Fact]
    public void FR_AUTH_5_Account_switch_dialog_opens_and_shows_what_will_be_deleted()
    {
        var result = OnSta(_assessment);

        result.Error.Should().BeNull(result.Error?.ToString());
        result.Title.Should().Be("Đổi sang tài khoản Google khác?");
        result.Subtitle.Should().Contain("loc.dang@gmail.com").And.Contain("an.nguyen@gmail.com");
        result.WarningVisible.Should().BeTrue();
        result.BulletCount.Should().Be(3);
        result.KeepIsDefaultAndCancel.Should().BeTrue("a stray Enter or Escape must keep the current account");
    }

    [Fact]
    public void FR_AUTH_5_Account_switch_dialog_hides_the_warning_box_when_only_the_cache_would_be_lost()
    {
        var result = OnSta(_assessment with { PendingChanges = 0, PendingAiCommands = 0, LocalLunarRules = 0 });

        result.Error.Should().BeNull(result.Error?.ToString());
        result.WarningVisible.Should().BeFalse();
    }

    private sealed record Outcome(Exception? Error, string? Title, string? Subtitle, bool WarningVisible, int BulletCount, bool KeepIsDefaultAndCancel);

    private static Outcome OnSta(AccountSignInAssessment assessment)
    {
        Outcome? outcome = null;
        Application? app = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Same bootstrap as EventEditorDialogTests: the dialog resolves tokens and styles through Application
                // resources, as at runtime. Non-parallel collection; the dictionaries are removed afterwards.
                app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Tokens.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Themes/Light.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Controls.xaml", UriKind.Relative) });

                var dialog = new AccountSwitchDialog(new AccountSwitchDialogViewModel(assessment, new LocalizationService(SupportedCultures.Vietnamese)));
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(568, 900));
                content.Arrange(new Rect(new Size(568, 900)));
                content.UpdateLayout();

                var warning = Find<Border>(content, "SwitchWarning");
                var keep = Find<Button>(content, "BtnSwitchKeep");
                var confirm = Find<Button>(content, "BtnSwitchConfirm");
                outcome = new Outcome(
                    null,
                    Find<TextBlock>(content, "SwitchTitle").Text,
                    Find<TextBlock>(content, "SwitchSubtitle").Text,
                    warning.Visibility == Visibility.Visible,
                    ((ItemsControl)((StackPanel)warning.Child).Children[1]).Items.Count,
                    keep.IsDefault && keep.IsCancel && !confirm.IsDefault && !confirm.IsCancel);
                dialog.Close();
            }
            catch (Exception ex)
            {
                outcome = new Outcome(ex, null, null, false, 0, false);
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

    private static T Find<T>(DependencyObject root, string automationId)
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
            return Find<T>(root, automationId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
