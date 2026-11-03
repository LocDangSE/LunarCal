using System.Windows.Controls;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views.Settings;

/// <summary>
/// Interaction logic for GoogleAccountPage.xaml (Figma 04).
/// </summary>
public partial class GoogleAccountPage : UserControl
{
    public GoogleAccountPage(GoogleAccountViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
