using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.Core.Calendar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing an individual event item within an agenda day group (Figma S08, FR-UI-1).
/// </summary>
public sealed partial class AgendaEventItemViewModel : ObservableObject
{
    /// <summary>The underlying calendar event.</summary>
    public CalendarEvent Event { get; }

    /// <summary>Event title.</summary>
    public string Title => Event.Title;

    /// <summary>Event location if present.</summary>
    public string? Location => Event.Location;

    /// <summary>Whether event has a location specified.</summary>
    public bool HasLocation => !string.IsNullOrWhiteSpace(Event.Location);

    /// <summary>Foreign key referencing the parent calendar.</summary>
    public string CalendarId => Event.CalendarId;

    /// <summary>Hex color of the calendar.</summary>
    public string CalendarColor { get; }

    /// <summary>Display name of the parent calendar.</summary>
    public string CalendarName { get; }

    /// <summary>Whether this is an all-day event.</summary>
    public bool AllDay => Event.AllDay;

    /// <summary>Formatted time display string (e.g. "Cả ngày" or "09:00 – 10:30").</summary>
    public string TimeDisplay { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AgendaEventItemViewModel"/>.
    /// </summary>
    public AgendaEventItemViewModel(
        CalendarEvent calendarEvent,
        string calendarColor,
        string calendarName,
        string timeDisplay)
    {
        Event = calendarEvent ?? throw new ArgumentNullException(nameof(calendarEvent));
        CalendarColor = string.IsNullOrWhiteSpace(calendarColor) ? "#2F6BFF" : calendarColor;
        CalendarName = calendarName;
        TimeDisplay = timeDisplay;
    }
}
