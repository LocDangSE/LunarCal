namespace LunarCal.Core.Calendar;

/// <summary>
/// Converts between UTC instants and calendar dates in a display time zone (FR-UI-7, FR-I18N-4).
/// All UI views use these helpers instead of fixed offsets so that a user-selected time zone and
/// daylight-saving transitions are honoured.
/// </summary>
public static class CalendarTime
{
    /// <summary>
    /// Returns the UTC instants of local midnight at the start of <paramref name="startDate"/> and at the start of
    /// <paramref name="endDateExclusive"/> in <paramref name="timeZone"/>.
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetUtcRange(
        DateOnly startDate,
        DateOnly endDateExclusive,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return (StartOfDayUtc(startDate, timeZone), StartOfDayUtc(endDateExclusive, timeZone));
    }

    /// <summary>
    /// Returns the UTC instant of the first moment of <paramref name="date"/> in <paramref name="timeZone"/>.
    /// If local midnight does not exist (DST gap), the first valid local time after the gap is used.
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Converts local date and time in <paramref name="timeZone"/> to a UTC instant.</summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var dt = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = timeZone.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset).ToUniversalTime();
    }

    /// <summary>Converts a UTC instant to local wall-clock time in <paramref name="timeZone"/>.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return TimeZoneInfo.ConvertTime(instant, timeZone);
    }

    /// <summary>Returns the calendar date of <paramref name="instant"/> in <paramref name="timeZone"/>.</summary>
    public static DateOnly ToLocalDate(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(ToLocal(instant, timeZone).DateTime);
    }

    /// <summary>
    /// Returns the first and last local dates covered by <paramref name="calendarEvent"/> in
    /// <paramref name="timeZone"/>. The end instant is exclusive, so an event ending exactly at midnight does not
    /// cover the following day. All-day events are evaluated in the event's own time zone when it is known,
    /// because their dates are fixed and must not shift with the viewer's time zone.
    /// </summary>
    public static (DateOnly First, DateOnly Last) GetCoveredDates(CalendarEvent calendarEvent, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        ArgumentNullException.ThrowIfNull(timeZone);

        var zone = calendarEvent.AllDay ? ResolveTimeZone(calendarEvent.TimeZone, timeZone) : timeZone;
        var first = ToLocalDate(calendarEvent.StartUtc, zone);
        var lastInstant = calendarEvent.EndUtc > calendarEvent.StartUtc
            ? calendarEvent.EndUtc.AddTicks(-1)
            : calendarEvent.StartUtc;
        var last = ToLocalDate(lastInstant, zone);
        return (first, last < first ? first : last);
    }

    /// <summary>Returns true when <paramref name="calendarEvent"/> covers <paramref name="date"/> in <paramref name="timeZone"/>.</summary>
    public static bool Covers(CalendarEvent calendarEvent, DateOnly date, TimeZoneInfo timeZone)
    {
        var (first, last) = GetCoveredDates(calendarEvent, timeZone);
        return date >= first && date <= last;
    }

    /// <summary>
    /// Finds a time zone by Windows or IANA identifier. Returns <paramref name="fallback"/> when the identifier is
    /// empty or unknown on this machine.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId, TimeZoneInfo fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return fallback;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return fallback;
        }
        catch (InvalidTimeZoneException)
        {
            return fallback;
        }
    }
}
