using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LunarCal.App.ViewModels;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for DayDetailPanel.xaml (Figma S09, FR-UI-3).
/// </summary>
public partial class DayDetailPanel : UserControl
{
    public DayDetailPanel()
    {
        InitializeComponent();
    }

    private void OnEventItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AgendaEventItemViewModel item)
        {
            (DataContext as DayDetailViewModel)?.SelectEvent(item);
        }
    }
}
