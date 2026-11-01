using System.Windows;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>Main shell window.</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the main window bound to its view model.</summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
