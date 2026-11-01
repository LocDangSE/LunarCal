using CommunityToolkit.Mvvm.ComponentModel;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model for an individual date cell within the sidebar mini calendar (FR-UI-1, FR-UI-10).
/// </summary>
public sealed partial class MiniCalendarDayViewModel : ObservableObject
{
    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private int _dayNumber;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isSelected;
}
