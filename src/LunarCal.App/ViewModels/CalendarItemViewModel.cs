using CommunityToolkit.Mvvm.ComponentModel;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing an individual calendar entry in the sidebar calendar list (FR-UI-1, FR-UI-10).
/// </summary>
public sealed partial class CalendarItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _color = CalendarColors.DefaultHex;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private bool _isPrimary;

    [ObservableProperty]
    private string? _accountId;
}
