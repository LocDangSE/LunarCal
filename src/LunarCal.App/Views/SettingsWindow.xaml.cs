using System.Windows;
using LunarCal.App.ViewModels;
using LunarCal.App.Views.Settings;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml (Figma 04, S13, S20).
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(
        SettingsViewModel viewModel,
        AppearancePage appearancePage,
        GoogleAccountPage accountPage,
        AdvancedPage advancedPage)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        AppearanceContent.Content = appearancePage ?? throw new ArgumentNullException(nameof(appearancePage));
        AccountContent.Content = accountPage ?? throw new ArgumentNullException(nameof(accountPage));
        AdvancedContent.Content = advancedPage ?? throw new ArgumentNullException(nameof(advancedPage));
    }
}
