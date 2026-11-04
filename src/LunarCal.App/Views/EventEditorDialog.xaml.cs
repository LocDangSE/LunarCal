using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for EventEditorDialog.xaml (Figma S10).
/// </summary>
public partial class EventEditorDialog : Window
{
    /// <summary>
    /// Gets the event editor view model.
    /// </summary>
    public EventEditorViewModel ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="EventEditorDialog"/>.
    /// </summary>
    public EventEditorDialog(EventEditorViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Saved += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        viewModel.Deleted += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        viewModel.Cancelled += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
    }

    // The window has no title bar (Figma S10 draws its own header), so the header drags the dialog.
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // "+ Thêm nhắc nhở" opens the preset list (at start, 10 min, 30 min, 1 h, 1 day) under the button.
    private void OnAddReminderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
