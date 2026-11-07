using System.Windows;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Core.Abstractions;

namespace LunarCal.App.Services;

/// <summary>
/// Asks the user whether a different Google account may replace the one stored on this machine, listing the data that
/// would be deleted (ADR-0029). Faked in tests.
/// </summary>
public interface IAccountSwitchPrompt
{
    /// <summary>Returns <see langword="true"/> only when the user chose to delete the old data and switch.</summary>
    Task<bool> ConfirmSwitchAsync(AccountSignInAssessment assessment);
}

/// <summary>Adapts an optional <see cref="IAccountSwitchPrompt"/> to the delegate <c>IAccountSessionService</c> expects.</summary>
public static class AccountSwitchPrompts
{
    /// <summary>A missing prompt means the switch is declined: data is never deleted without asking.</summary>
    public static Func<AccountSignInAssessment, CancellationToken, Task<bool>> ToDelegate(IAccountSwitchPrompt? prompt) =>
        (assessment, _) => prompt?.ConfirmSwitchAsync(assessment) ?? Task.FromResult(false);
}

/// <summary><see cref="IAccountSwitchPrompt"/> that shows <see cref="AccountSwitchDialog"/> over the active window.</summary>
public sealed class DialogAccountSwitchPrompt : IAccountSwitchPrompt
{
    private readonly ILocalizationService _localization;

    /// <summary>Creates the prompt.</summary>
    public DialogAccountSwitchPrompt(ILocalizationService localization)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    /// <inheritdoc />
    public Task<bool> ConfirmSwitchAsync(AccountSignInAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
        var dialog = new AccountSwitchDialog(new AccountSwitchDialogViewModel(assessment, _localization));
        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
