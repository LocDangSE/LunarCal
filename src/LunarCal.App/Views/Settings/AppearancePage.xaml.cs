using System.Windows;
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
    /// The view model loads the current settings when the page is shown.
    /// </summary>
    public AppearancePage(AppearanceSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += OnLoaded;
    }

    // Event handler: async void is allowed here (AGENTS.md §7). A failed load keeps the defaults already shown,
    // so the exception is contained instead of crashing the dispatcher.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is not AppearanceSettingsViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing else to do in Phase 2: the page still shows the current in-memory preferences.
        }
    }
}
