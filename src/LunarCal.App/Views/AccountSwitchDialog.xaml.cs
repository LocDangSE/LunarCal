using System.Windows;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Modal confirmation before a different Google account replaces the stored one (ADR-0029, Figma S14 1.5).
/// "Keep current account" is the default button and Escape, so a stray Enter never deletes data.
/// </summary>
public partial class AccountSwitchDialog : Window
{
    /// <summary>Creates the dialog for <paramref name="viewModel"/>.</summary>
    public AccountSwitchDialog(AccountSwitchDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnKeep(object sender, RoutedEventArgs e) => DialogResult = false;
}
