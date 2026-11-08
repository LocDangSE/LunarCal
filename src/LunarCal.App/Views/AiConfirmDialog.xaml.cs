using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for AiConfirmDialog.xaml (FR-CONFIRM-1..6, Figma 02 2:2 / 2:494,
/// and FR-CONFIRM-8, Figma S16 46:370 / dialog 46:862).
/// In clarification mode, automatically focuses the short answer input box on load.
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
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsClarificationMode)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                ClarifyInputBox.Focus();
                Keyboard.Focus(ClarifyInputBox);
            }));
        }
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
