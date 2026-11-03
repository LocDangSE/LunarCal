using System.Windows.Controls;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views.Settings;

/// <summary>
/// Interaction logic for AdvancedPage.xaml (Figma S20).
/// </summary>
public partial class AdvancedPage : UserControl
{
    public AdvancedPage(AdvancedSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
