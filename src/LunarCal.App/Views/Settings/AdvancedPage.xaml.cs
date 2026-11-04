using System.Windows;
using System.Windows.Controls;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views.Settings;

/// <summary>Settings > Nâng cao (Figma S20); DataContext is the <c>SettingsViewModel</c>.</summary>
public partial class AdvancedPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AdvancedPage() => InitializeComponent();

    // A PasswordBox cannot be bound (by design); copy the typed secret into the view model.
    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox box)
        {
            vm.Advanced.ClientSecret = box.Password;
        }
    }
}
