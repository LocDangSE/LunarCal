using LunarCal.Core.Calendar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// Interaction mode for launching the event editor dialog (Figma S10).
/// </summary>
public enum EventEditorMode
{
    Create,
    Edit,
    CreateLunarRule
}

/// <summary>
/// Parameter payload for opening the event editor dialog.
/// </summary>
public sealed record EventEditorRequest(
    EventEditorMode Mode,
    DateOnly? Date = null,
    TimeOnly? Time = null,
    bool IsAllDay = false,
    CalendarEvent? Event = null,
    string? CalendarId = null,
    EventEditorPrefill? Prefill = null);
