using System.Windows.Controls;
using System.Windows.Input;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Controls;

/// <summary>
/// Interaction logic for AiCommandBar.xaml (FR-UI-5, FR-QUEUE-1, FR-I18N-2).
/// </summary>
public partial class AiCommandBar : UserControl
{
    public AiCommandBar()
    {
        InitializeComponent();
    }

    private void OnCommandTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (DataContext is AiCommandBarViewModel vm && vm.SendCommandCommand.CanExecute(null))
            {
                vm.SendCommandCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
