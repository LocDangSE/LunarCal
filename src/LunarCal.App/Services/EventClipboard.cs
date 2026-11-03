using LunarCal.Core.Calendar;

namespace LunarCal.App.Services;

/// <summary>
/// In-memory clipboard for copying and pasting events between calendar slots (Figma S10, FR-UI-4).
/// </summary>
public static class EventClipboard
{
    /// <summary>
    /// Gets or sets the event currently copied in the in-app clipboard.
    /// </summary>
    public static CalendarEvent? Current { get; set; }

    /// <summary>
    /// Gets whether an event is currently stored on the clipboard.
    /// </summary>
    public static bool HasEvent => Current is not null;

    /// <summary>
    /// Copies an event to the clipboard.
    /// </summary>
    public static void Copy(CalendarEvent calendarEvent)
    {
        Current = calendarEvent;
    }

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    public static void Clear()
    {
        Current = null;
    }
}
