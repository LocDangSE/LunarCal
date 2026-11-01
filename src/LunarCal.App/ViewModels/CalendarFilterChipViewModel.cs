using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing an interactive calendar filter chip in AgendaView (Figma S08, FR-UI-1).
/// </summary>
public sealed partial class CalendarFilterChipViewModel : ObservableObject
{
    /// <summary>Calendar identifier.</summary>
    public string Id { get; }

    /// <summary>Calendar display summary or name.</summary>
    public string Summary { get; }

    /// <summary>Calendar bullet hex color.</summary>
    public string Color { get; }

    /// <summary>Whether this calendar is currently included in the filtered agenda list.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Event raised when filter selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="CalendarFilterChipViewModel"/>.
    /// </summary>
    public CalendarFilterChipViewModel(string id, string summary, string color, bool isSelected = true)
    {
        Id = id;
        Summary = summary;
        Color = string.IsNullOrWhiteSpace(color) ? CalendarColors.DefaultHex : color;
        IsSelected = isSelected;
    }

    /// <summary>
    /// Toggles active selection of this calendar filter.
    /// </summary>
    [RelayCommand]
    public void Toggle()
    {
        IsSelected = !IsSelected;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
