namespace LunarCal.App.ViewModels;

/// <summary>
/// Supported calendar view modes in the application shell (FR-UI-1, FR-UI-9).
/// </summary>
public enum CalendarViewMode
{
    /// <summary>Single day view.</summary>
    Day,

    /// <summary>7-day week view.</summary>
    Week,

    /// <summary>Month grid view (default).</summary>
    Month,

    /// <summary>Agenda list view.</summary>
    Agenda,
}
