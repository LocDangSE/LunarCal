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
        Color = string.IsNullOrWhiteSpace(color) ? "#2F6BFF" : color;
        TimeRangeText = timeRangeText;
    }
}
