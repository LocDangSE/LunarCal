using System.Windows;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>Settings window (Figma 04, S13, S20). Pages are declared in XAML and bound to the child view models.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Creates the window bound to <paramref name="viewModel"/>.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();

        // After "Xoá toàn bộ dữ liệu cục bộ" the caches and keys the running app holds are gone: exit (SEC-DATA-3).
        viewModel.Advanced.WipeCompleted += (_, _) => Application.Current?.Shutdown();
        Loaded += OnLoaded;
    }

    // Event handler: async void is allowed here (AGENTS.md §7). A failed load keeps the values already shown.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        try
        {
            await vm.Appearance.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            vm.Appearance.SaveErrorText = ex.Message;
        }
    }
}
