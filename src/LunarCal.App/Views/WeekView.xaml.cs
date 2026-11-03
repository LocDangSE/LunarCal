using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LunarCal.App.ViewModels;
using LunarCal.Core.Calendar;

namespace LunarCal.App.Views;

/// <summary>
/// Interaction logic for WeekView.xaml (Figma S06, FR-UI-1, FR-UI-4, FR-UI-8).
/// </summary>
public partial class WeekView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private Border? _draggedBorder;
    private TimedEventViewModel? _draggedVm;
    private CalendarEvent? _contextMenuEvent;
    private (DateOnly Date, TimeOnly Time) _contextMenuSlot;

    public WeekView()
    {
        InitializeComponent();
    }

    private WeekViewModel? ViewModel => DataContext as WeekViewModel;

    private void OnEventContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is TimedEventViewModel timedVm)
            {
                _contextMenuEvent = timedVm.Event;
            }
            else if (fe.DataContext is EventChipViewModel chipVm)
            {
                _contextMenuEvent = chipVm.Event;
            }
        }
    }

    private void OnSlotContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DayColumnViewModel col)
        {
            var pos = Mouse.GetPosition(fe);
            int hour = Math.Clamp((int)(pos.Y / 48.0), 0, 23);
            int quarter = Math.Clamp((int)((pos.Y % 48.0) / 12.0), 0, 3);
            _contextMenuSlot = (col.Date, new TimeOnly(hour, quarter * 15));
        }
    }

    private void OnCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DayColumnViewModel col)
        {
            var pos = e.GetPosition(fe);
            int hour = Math.Clamp((int)(pos.Y / 48.0), 0, 23);
            int quarter = Math.Clamp((int)((pos.Y % 48.0) / 12.0), 0, 3);
            _contextMenuSlot = (col.Date, new TimeOnly(hour, quarter * 15));
        }
    }

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is DayColumnViewModel col)
        {
            var pos = e.GetPosition(fe);
            int hour = Math.Clamp((int)(pos.Y / 48.0), 0, 23);
            int quarter = Math.Clamp((int)((pos.Y % 48.0) / 12.0), 0, 3);
            ViewModel?.CreateEventAt(col.Date, new TimeOnly(hour, quarter * 15));
            e.Handled = true;
        }
    }

    private void OnAllDayEventMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is EventChipViewModel chipVm && chipVm.Event is not null)
        {
            ViewModel?.OpenEvent(chipVm.Event);
            e.Handled = true;
        }
    }

    private void OnTimedEventPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is TimedEventViewModel vm)
        {
            if (e.ClickCount == 2)
            {
                ViewModel?.OpenEvent(vm.Event);
                e.Handled = true;
                return;
            }

            _dragStartPoint = e.GetPosition(this);
            _draggedBorder = border;
            _draggedVm = vm;
            _isDragging = false;
        }
    }

    private void OnTimedEventPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedBorder is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var currentPoint = e.GetPosition(this);
            var deltaY = currentPoint.Y - _dragStartPoint.Y;

            if (!_isDragging && Math.Abs(deltaY) > 6)
            {
                _isDragging = true;
                _draggedBorder.CaptureMouse();
            }

            if (_isDragging)
            {
                _draggedBorder.RenderTransform = new TranslateTransform(0, deltaY);
            }
        }
    }

    private async void OnTimedEventPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedBorder is not null)
        {
            if (_isDragging && _draggedVm is not null)
            {
                _draggedBorder.ReleaseMouseCapture();
                _draggedBorder.RenderTransform = Transform.Identity;

                var currentPoint = e.GetPosition(this);
                var deltaY = currentPoint.Y - _dragStartPoint.Y;

                // 48 px per hour = 12 px per 15-minute slot
                int quarters = (int)Math.Round(deltaY / 12.0);
                var timeDelta = TimeSpan.FromMinutes(quarters * 15);

                if (timeDelta != TimeSpan.Zero && ViewModel is not null)
                {
                    await ViewModel.AdjustEventTimeAsync(_draggedVm.Event.Id, timeDelta);
                }
            }

            _isDragging = false;
            _draggedBorder = null;
            _draggedVm = null;
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
        ViewModel?.CreateEventAt(_contextMenuSlot.Date, _contextMenuSlot.Time);
    }

    private void OnCreateAllDayClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateEventAllDay(_contextMenuSlot.Date);
    }

    private void OnCreateLunarClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateLunarRule(_contextMenuSlot.Date);
    }

    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.PasteEventAsync(_contextMenuSlot.Date, _contextMenuSlot.Time);
        }
    }
}
