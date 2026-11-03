using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LunarCal.App.ViewModels;
using LunarCal.Core.Calendar;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for MonthView.xaml (Figma 01, FR-UI-1, FR-UI-4, FR-UI-8).
/// </summary>
public partial class MonthView : UserControl
{
    private CalendarEvent? _contextMenuEvent;
    private DateOnly _contextMenuDate;

    public MonthView()
    {
        InitializeComponent();
    }

    private MonthViewModel? ViewModel => DataContext as MonthViewModel;

    private void OnEventContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EventChipViewModel chipVm)
        {
            _contextMenuEvent = chipVm.Event;
        }
    }

    private void OnSlotContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MonthDayViewModel dayVm)
        {
            _contextMenuDate = dayVm.SolarDate;
        }
    }

    private void OnDayCellMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MonthDayViewModel dayVm)
        {
            ViewModel?.SelectDay(dayVm);

            if (e.ClickCount == 2)
            {
                ViewModel?.CreateEventAllDay(dayVm.SolarDate);
                e.Handled = true;
            }
        }
    }

    private void OnEventChipMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EventChipViewModel chipVm && chipVm.Event is not null)
        {
            if (e.ClickCount == 2)
            {
                ViewModel?.OpenEvent(chipVm.Event);
                e.Handled = true;
            }
        }
    }

    private void OnEditEventClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent is not null)
        {
            ViewModel?.OpenEvent(_contextMenuEvent);
        }
    }

    private async void OnDuplicateEventClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent is not null && ViewModel is not null)
        {
            await ViewModel.DuplicateEventAsync(_contextMenuEvent);
        }
    }

    private void OnConvertLunarClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent is not null && ViewModel is not null)
        {
            var date = DateOnly.FromDateTime(_contextMenuEvent.StartUtc.Date);
            ViewModel.CreateLunarRule(date);
        }
    }

    private async void OnDeleteEventClick(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent is not null && ViewModel is not null)
        {
            await ViewModel.DeleteEventAsync(_contextMenuEvent);
        }
    }

    private void OnCreateEventAtClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateEventAt(_contextMenuDate, new TimeOnly(9, 0));
    }

    private void OnCreateAllDayClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateEventAllDay(_contextMenuDate);
    }

    private void OnCreateLunarClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateLunarRule(_contextMenuDate);
    }

    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.PasteEventAsync(_contextMenuDate);
        }
    }
}
