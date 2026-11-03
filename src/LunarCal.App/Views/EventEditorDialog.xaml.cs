using System.Windows;
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
}
