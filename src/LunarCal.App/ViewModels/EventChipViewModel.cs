using CommunityToolkit.Mvvm.ComponentModel;

namespace LunarCal.App.ViewModels;

/// <summary>
/// One of the fixed chip slots of a month cell. Cells keep the same three slots and only change their content,
/// so WPF does not regenerate chip containers on every month switch (NFR-PERF, FR-UI-1).
/// </summary>
public sealed partial class EventChipViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isVisible;
}
