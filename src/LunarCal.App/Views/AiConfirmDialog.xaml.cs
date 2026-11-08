using System;
using System.Windows;
using System.Windows.Input;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for AiConfirmDialog.xaml (FR-CONFIRM-1..6, Figma 02 2:2 / 2:494).
/// </summary>
public partial class AiConfirmDialog : Window
{
    public AiConfirmDialogViewModel ViewModel { get; }

    public AiConfirmDialog(AiConfirmDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        ViewModel.CloseRequested += OnCloseRequested;
        MouseDown += OnWindowMouseDown;
    }

    private void OnCloseRequested(object? sender, bool? result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // If shown non-modally in certain test scenarios
            Close();
        }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
