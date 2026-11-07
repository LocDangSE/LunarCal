using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.Core.Calendar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// An event chip (Figma 01 month cell, S06 all-day row): label, calendar colours and the AI sparkle. Month cells
/// keep three fixed slots and only change their content, so WPF does not regenerate chip containers on every
/// month switch (NFR-PERF, FR-UI-1).
/// </summary>
public sealed partial class EventChipViewModel : ObservableObject
{
    /// <summary>The underlying calendar event domain model if active.</summary>
    public CalendarEvent? Event { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Chip background (hex), the subtle tint of the calendar colour.</summary>
    [ObservableProperty]
    private string _background = CalendarColors.Palette(null).Subtle;

    /// <summary>Chip text colour (hex), the dark shade of the calendar colour.</summary>
    [ObservableProperty]
    private string _foreground = CalendarColors.Palette(null).Text;

    /// <summary>True for events created by the AI command bar; the chip shows the sparkle icon.</summary>
    [ObservableProperty]
    private bool _isAi;

    /// <summary>Shows <paramref name="calendarEvent"/> in this chip.</summary>
    public void Show(CalendarEvent calendarEvent, string label, string? calendarColor)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        Event = calendarEvent;
        var palette = CalendarColors.Palette(calendarColor);
        Title = label;
        Background = palette.Subtle;
        Foreground = palette.Text;
        IsAi = CalendarColors.IsAiEvent(calendarEvent);
        IsVisible = true;
    }

    /// <summary>Hides this chip slot.</summary>
    public void Hide()
    {
        Event = null;
        Title = string.Empty;
        IsAi = false;
        IsVisible = false;
    }

    /// <summary>Creates a visible chip for <paramref name="calendarEvent"/>.</summary>
    public static EventChipViewModel For(CalendarEvent calendarEvent, string label, string? calendarColor)
    {
        var chip = new EventChipViewModel();
        chip.Show(calendarEvent, label, calendarColor);
        return chip;
    }
}
