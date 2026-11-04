using System.Windows;
using LunarCal.App.Services;

namespace LunarCal.App.Views;

/// <summary>Modal confirmation (FR-AUTH-4, SEC-DATA-3): Cancel is the default, Escape cancels.</summary>
public partial class ConfirmDialog : Window
{
    /// <summary>Creates the dialog for <paramref name="request"/>.</summary>
    public ConfirmDialog(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        DataContext = request;
        ConfirmButton.Style = (Style)FindResource(request.IsDestructive ? "DangerButtonStyle" : "PrimaryButtonStyle");
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
