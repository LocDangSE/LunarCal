namespace LunarCal.Core.Calendar;

/// <summary>
/// Supported recurrence types for calendar events (FR-UI-4, FR-LUNAR-4, FR-LUNAR-5).
/// </summary>
public enum EventRecurrenceType
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
    Lunar = 5
}
