using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.Core.Calendar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing an event positioned on the timed hourly grid (FR-UI-1).
/// </summary>
public sealed partial class TimedEventViewModel : ObservableObject
{
    /// <summary>The underlying calendar event domain model.</summary>
    public CalendarEvent Event { get; }

    /// <summary>Title of the event.</summary>
    public string Title => Event.Title;

    /// <summary>Display hex color for the event.</summary>
    public string Color { get; }

    /// <summary>Block background (hex), the subtle tint of the calendar colour (Figma S06).</summary>
    public string Background => CalendarColors.Palette(Color).Subtle;

    /// <summary>Block text colour (hex), the dark shade of the calendar colour (Figma S06).</summary>
    public string Foreground => CalendarColors.Palette(Color).Text;

    /// <summary>True for events created by the AI command bar; the block shows the sparkle.</summary>
    public bool IsAi => CalendarColors.IsAiEvent(Event);

    /// <summary>Start time converted to the display timezone offset.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>End time converted to the display timezone offset.</summary>
    public DateTimeOffset End { get; }

    /// <summary>Top offset in pixels from 00:00 (HourRowHeight = 48.0 px).</summary>
    public double Top { get; }

    /// <summary>Height in pixels corresponding to duration (minimum 24.0 px).</summary>
    public double Height { get; }

    /// <summary>Formatted time range string, e.g. "09:00 – 10:30".</summary>
    public string TimeRangeText { get; }

    /// <summary>Name of the event's calendar, e.g. "Cá nhân · Google".</summary>
    public string CalendarName { get; init; } = string.Empty;

    /// <summary>Day view meta line (Figma S07 25:93): "14:00 – 15:30 · Cá nhân · Google".</summary>
    public string DetailText => string.IsNullOrEmpty(CalendarName) ? TimeRangeText : $"{TimeRangeText} · {CalendarName}";

    /// <summary>0-based column lane index within an overlapping event cluster.</summary>
    [ObservableProperty]
    private int _columnIndex;

    /// <summary>Total number of column lanes in the overlapping cluster.</summary>
    [ObservableProperty]
    private int _totalColumns = 1;

    /// <summary>
    /// Initializes a new instance of <see cref="TimedEventViewModel"/>.
    /// </summary>
    public TimedEventViewModel(
        CalendarEvent calendarEvent,
        DateTimeOffset start,
        DateTimeOffset end,
        double top,
        double height,
        string color,
        string timeRangeText)
    {
        Event = calendarEvent ?? throw new ArgumentNullException(nameof(calendarEvent));
        Start = start;
        End = end;
        Top = top;
        Height = height;
        Color = string.IsNullOrWhiteSpace(color) ? CalendarColors.DefaultHex : color;
        TimeRangeText = timeRangeText;
    }
}
