using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing a single day column in WeekView or DayView (FR-UI-1, FR-UI-2, FR-LUNAR-6).
/// </summary>
public sealed partial class DayColumnViewModel : ObservableObject
{
    /// <summary>Height of each 1-hour row in pixels (from design tokens).</summary>
    public const double HourRowHeight = 48.0;

    /// <summary>Hour shown at the top of the time grid when a view opens (Figma S06/S07 start at 07:00).</summary>
    public const int FirstVisibleHour = 7;

    /// <summary>Minimum height of a timed event chip in pixels.</summary>
    public const double MinEventHeight = 24.0;

    private readonly ILocalizationService _localization;
    private readonly string _leapMarker;
    private readonly string _lunarLabelFormat;

    /// <summary>Gregorian solar date of this day column.</summary>
    public DateOnly Date { get; }

    /// <summary>Calculated Vietnamese lunar date (null if out of supported range).</summary>
    public LunarDate? LunarDate { get; }

    /// <summary>Day of month in lunar calendar.</summary>
    public int LunarDay => LunarDate?.Day ?? 0;

    /// <summary>Month number in lunar calendar.</summary>
    public int LunarMonth => LunarDate?.Month ?? 0;

    /// <summary>Whether this lunar month is a leap month.</summary>
    public bool IsLeapMonth => LunarDate?.IsLeapMonth ?? false;

    /// <summary>Formatted day of week name (e.g. "Thứ Hai", "Mon").</summary>
    public string DayOfWeekName { get; }

    /// <summary>Day of month number as string.</summary>
    public string SolarDayString { get; }

    /// <summary>Whether this date is the current local today.</summary>
    public bool IsToday { get; }

    /// <summary>Formatted lunar day text (e.g. "10", "1/9", "1/6n").</summary>
    [ObservableProperty]
    private string _lunarDayText = string.Empty;

    /// <summary>Lunar label for the day view header, e.g. "Âm lịch: 7" / "Lunar: 7" (FR-I18N-5).</summary>
    [ObservableProperty]
    private string _lunarLabelText = string.Empty;

    /// <summary>Whether lunar dates are visible per global settings (FR-LUNAR-6).</summary>
    [ObservableProperty]
    private bool _isLunarVisible = true;

    /// <summary>Whether this day column is selected.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether to display the current time indicator line (red Now line).</summary>
    [ObservableProperty]
    private bool _showNowIndicator;

    /// <summary>Top offset in pixels for the current time indicator.</summary>
    [ObservableProperty]
    private double _nowIndicatorTop = -1;

    /// <summary>Events spanning the whole day, as chips for the all-day row (Figma S06 24:48).</summary>
    public ObservableCollection<EventChipViewModel> AllDayEvents { get; } = new();

    /// <summary>Two-letter weekday for the week column header (Figma S06: "T6").</summary>
    public string ShortDayName { get; }

    /// <summary>Day view column header (Figma S07): "Thứ Sáu · Hôm nay" for today, else "Thứ Sáu".</summary>
    public string DayHeaderText { get; }

    /// <summary>Day view lunar line (Figma S07): "Mùng 7 tháng 9 · Quý Hợi".</summary>
    [ObservableProperty]
    private string _lunarHeaderText = string.Empty;

    /// <summary>True for mùng 1 and rằm, which Figma sets in SemiBold.</summary>
    public bool IsLunarHighlight => LunarDay is 1 or 15;

    /// <summary>Timed events positioned on the 24-hour vertical grid.</summary>
    public ObservableCollection<TimedEventViewModel> TimedEvents { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="DayColumnViewModel"/>.
    /// </summary>
    public DayColumnViewModel(
        DateOnly date,
        LunarDate? lunarDate,
        string dayOfWeekName,
        bool isToday,
        bool isLunarVisible,
        string? leapMarker = null,
        ILocalizationService? localizationService = null)
    {
        _localization = localizationService ?? LocalizationService.Instance;
        _leapMarker = leapMarker ?? CalendarText.LeapMarker(_localization);
        _lunarLabelFormat = _localization["lunar.day_label"];
        ShortDayName = CalendarText.WeekdayMin(_localization, date.DayOfWeek);
        DayHeaderText = isToday ? _localization.Format("day.header_today", dayOfWeekName) : dayOfWeekName;
        Date = date;
        LunarDate = lunarDate;
        DayOfWeekName = dayOfWeekName;
        SolarDayString = date.Day.ToString(CultureInfo.InvariantCulture);
        IsToday = isToday;
        IsLunarVisible = isLunarVisible;

        UpdateLunarText();
        UpdateNowIndicator();
    }

    /// <summary>
    /// Toggles lunar text visibility (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        UpdateLunarText();
    }

    /// <summary>
    /// Formats lunar day string: mùng 1 as "1/tháng" (with "n" if leap month), otherwise day number (FR-UI-2).
    /// </summary>
    public void UpdateLunarText()
    {
        if (!IsLunarVisible || LunarDate is null)
        {
            LunarDayText = string.Empty;
            LunarLabelText = string.Empty;
            LunarHeaderText = string.Empty;
            return;
        }

        LunarDayText = CalendarText.LunarCell(LunarDate, _leapMarker);
        LunarLabelText = string.Format(CultureInfo.CurrentCulture, _lunarLabelFormat, LunarDayText);
        LunarHeaderText = _localization.Format(
            "format.dot_pair",
            CalendarText.LunarDayMonth(_localization, LunarDate.Value),
            LunarCalendar.GetDayCanChi(Date));
    }

    /// <summary>
    /// Computes the top pixel offset for the current time indicator if this day is today.
    /// </summary>
    public void UpdateNowIndicator(DateTimeOffset? now = null, TimeZoneInfo? timeZone = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        if (timeZone is not null)
        {
            current = CalendarTime.ToLocal(current, timeZone);
        }
        else if (now is null)
        {
            current = current.ToLocalTime();
        }

        var todayDate = DateOnly.FromDateTime(current.DateTime);

        if (Date == todayDate)
        {
            ShowNowIndicator = true;
            var minutes = current.Hour * 60.0 + current.Minute + current.Second / 60.0;
            NowIndicatorTop = (minutes / 60.0) * HourRowHeight;
        }
        else
        {
            ShowNowIndicator = false;
            NowIndicatorTop = -1;
        }
    }

    /// <summary>
    /// Populates all-day and timed events for this day, calculating positions and clustering lanes.
    /// Times are converted to <paramref name="timeZone"/> (the display time zone, FR-UI-7) and formatted with
    /// <paramref name="timeFormat"/> in <paramref name="culture"/> (FR-I18N-4).
    /// </summary>
    public void PopulateEvents(
        IReadOnlyList<CalendarEvent> events,
        TimeZoneInfo timeZone,
        string? timeFormat = null,
        CultureInfo? culture = null,
        IReadOnlyDictionary<string, CalendarInfo>? calendars = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeZone);

        AllDayEvents.Clear();
        TimedEvents.Clear();

        var dayStart = CalendarTime.ToLocal(CalendarTime.StartOfDayUtc(Date, timeZone), timeZone);
        var dayEnd = CalendarTime.ToLocal(CalendarTime.StartOfDayUtc(Date.AddDays(1), timeZone), timeZone);
        var dayMinutes = (dayEnd - dayStart).TotalMinutes;

        var timedList = new List<TimedEventViewModel>();

        foreach (var ev in events)
        {
            if (!CalendarTime.Covers(ev, Date, timeZone))
            {
                continue;
            }

            var calendar = calendars != null && calendars.TryGetValue(ev.CalendarId, out var c) ? c : null;
            var color = calendar?.Color;
            if (ev.AllDay)
            {
                AllDayEvents.Add(EventChipViewModel.For(ev, ev.Title, color));
                continue;
            }

            var evStart = CalendarTime.ToLocal(ev.StartUtc, timeZone);
            var evEnd = CalendarTime.ToLocal(ev.EndUtc, timeZone);

            // Clamp to this day boundary
            if (evStart < dayStart)
            {
                evStart = dayStart;
            }

            if (evEnd > dayEnd)
            {
                evEnd = dayEnd;
            }

            if (evEnd <= evStart)
            {
                evEnd = evStart.AddMinutes(15);
            }

            var startMinute = Math.Min((evStart - dayStart).TotalMinutes, dayMinutes);
            var durationMinutes = (evEnd - evStart).TotalMinutes;

            var top = (startMinute / 60.0) * HourRowHeight;
            var height = Math.Max(MinEventHeight, (durationMinutes / 60.0) * HourRowHeight);

            var timeText = TimeFormattingHelper.FormatTimeRange(
                CalendarTime.ToLocal(ev.StartUtc, timeZone),
                CalendarTime.ToLocal(ev.EndUtc, timeZone),
                timeFormat,
                culture);
            timedList.Add(new TimedEventViewModel(ev, evStart, evEnd, top, height, color ?? CalendarColors.DefaultHex, timeText)
            {
                CalendarName = calendar?.Summary ?? string.Empty,
            });
        }

        AssignLanes(timedList);

        foreach (var item in timedList)
        {
            TimedEvents.Add(item);
        }
    }

    /// <summary>
    /// Clusters overlapping timed events and assigns lane columns so overlapping events don't obscure each other (S06).
    /// </summary>
    public static void AssignLanes(List<TimedEventViewModel> timedEvents)
    {
        if (timedEvents.Count == 0)
        {
            return;
        }

        var sorted = timedEvents.OrderBy(e => e.Top).ThenByDescending(e => e.Height).ToList();

        var clusters = new List<List<TimedEventViewModel>>();
        List<TimedEventViewModel>? currentCluster = null;
        double clusterEnd = -1;

        foreach (var ev in sorted)
        {
            var evEnd = ev.Top + ev.Height;
            if (currentCluster == null || ev.Top >= clusterEnd)
            {
                currentCluster = new List<TimedEventViewModel> { ev };
                clusters.Add(currentCluster);
                clusterEnd = evEnd;
            }
            else
            {
                currentCluster.Add(ev);
                if (evEnd > clusterEnd)
                {
                    clusterEnd = evEnd;
                }
            }
        }

        foreach (var cluster in clusters)
        {
            var laneEnds = new List<double>();

            foreach (var ev in cluster)
            {
                var evEnd = ev.Top + ev.Height;
                int assignedLane = -1;

                for (int i = 0; i < laneEnds.Count; i++)
                {
                    if (laneEnds[i] <= ev.Top)
                    {
                        assignedLane = i;
                        laneEnds[i] = evEnd;
                        break;
                    }
                }

                if (assignedLane == -1)
                {
                    assignedLane = laneEnds.Count;
                    laneEnds.Add(evEnd);
                }

                ev.ColumnIndex = assignedLane;
            }

            int totalLanes = Math.Max(1, laneEnds.Count);
            foreach (var ev in cluster)
            {
                ev.TotalColumns = totalLanes;
            }
        }
    }
}
