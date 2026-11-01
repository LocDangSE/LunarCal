using System.Windows.Controls;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views.Settings;

/// <summary>
/// Interaction logic for AppearancePage.xaml (Figma S13, FR-UI-7, FR-I18N-4).
/// </summary>
public partial class AppearancePage : UserControl
{
    /// <summary>
    /// Initializes a new instance of <see cref="AppearancePage"/>.
    /// </summary>
    public AppearancePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AppearancePage"/> with a data context.
    /// </summary>
    public AppearancePage(AppearanceSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
