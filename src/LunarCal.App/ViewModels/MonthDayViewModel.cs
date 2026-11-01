using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Calendar;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing a single day cell in the 6×7 month calendar grid (FR-UI-1, FR-UI-2, FR-LUNAR-6).
/// </summary>
public sealed partial class MonthDayViewModel : ObservableObject
{
    private string _leapMarker;

    /// <summary>The Gregorian solar date for this cell.</summary>
    public DateOnly SolarDate { get; private set; }

    /// <summary>Gregorian day of month (1–31).</summary>
    public int SolarDay => SolarDate.Day;

    /// <summary>Formatted Gregorian day string.</summary>
    public string SolarDayString => SolarDay.ToString(CultureInfo.InvariantCulture);

    /// <summary>The calculated Vietnamese lunar date, or null if outside supported range.</summary>
    public LunarDate? LunarDate { get; private set; }

    /// <summary>Lunar day of month (1–30).</summary>
    public int LunarDay => LunarDate?.Day ?? 0;

    /// <summary>Lunar month number (1–12).</summary>
    public int LunarMonth => LunarDate?.Month ?? 0;

    /// <summary>Whether this lunar month is a leap month.</summary>
    public bool IsLeapMonth => LunarDate?.IsLeapMonth ?? false;

    /// <summary>True if this is the first day of a lunar month (mùng 1 âm).</summary>
    public bool IsFirstLunarDay => LunarDay == 1;

    [ObservableProperty]
    private string _lunarDayText = string.Empty;

    [ObservableProperty]
    private bool _isLunarVisible = true;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private IReadOnlyList<CalendarEvent> _events = Array.Empty<CalendarEvent>();

    [ObservableProperty]
    private IReadOnlyList<CalendarEvent> _visibleEvents = Array.Empty<CalendarEvent>();

    /// <summary>Maximum number of event chips shown in a cell before the "+N more" counter.</summary>
    public const int MaxVisibleChips = 3;

    /// <summary>The fixed chip slots bound by the month view (always <see cref="MaxVisibleChips"/> items).</summary>
    public IReadOnlyList<EventChipViewModel> Chips { get; } =
        Enumerable.Range(0, MaxVisibleChips).Select(_ => new EventChipViewModel()).ToArray();

    [ObservableProperty]
    private int _moreEventsCount;

    [ObservableProperty]
    private bool _hasMoreEvents;

    [ObservableProperty]
    private string _moreEventsText = string.Empty;

    /// <summary>
    /// Initializes a new instance of <see cref="MonthDayViewModel"/>.
    /// </summary>
    public MonthDayViewModel(
        DateOnly solarDate,
        LunarDate? lunarDate,
        bool isCurrentMonth,
        bool isToday,
        bool isLunarVisible,
        string? leapMarker = null)
    {
        _leapMarker = leapMarker ?? CalendarText.LeapMarker(LocalizationService.Instance);
        SolarDate = solarDate;
        LunarDate = lunarDate;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        IsLunarVisible = isLunarVisible;

        UpdateLunarDayText();
    }

    /// <summary>
    /// Re-targets this cell to another date so the month grid can reuse its 42 cells (and their WPF containers)
    /// when the month changes, instead of regenerating them (NFR-PERF).
    /// </summary>
    public void Reset(
        DateOnly solarDate,
        LunarDate? lunarDate,
        bool isCurrentMonth,
        bool isToday,
        bool isLunarVisible,
        string leapMarker)
    {
        SolarDate = solarDate;
        LunarDate = lunarDate;
        _leapMarker = leapMarker;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        IsSelected = false;
        IsLunarVisible = isLunarVisible;

        OnPropertyChanged(nameof(SolarDate));
        OnPropertyChanged(nameof(SolarDay));
        OnPropertyChanged(nameof(SolarDayString));
        OnPropertyChanged(nameof(LunarDate));
        OnPropertyChanged(nameof(LunarDay));
        OnPropertyChanged(nameof(LunarMonth));
        OnPropertyChanged(nameof(IsLeapMonth));
        OnPropertyChanged(nameof(IsFirstLunarDay));
        UpdateLunarDayText();
    }

    /// <summary>
    /// Updates whether the lunar day text is visible according to the global setting (FR-LUNAR-6).
    /// </summary>
    public void SetLunarVisibility(bool visible)
    {
        IsLunarVisible = visible;
        UpdateLunarDayText();
    }

    /// <summary>
    /// Updates the events assigned to this day and computes visible chips.
    /// </summary>
    public void SetEvents(IReadOnlyList<CalendarEvent> events, string moreEventsFormat)
    {
        Events = events ?? Array.Empty<CalendarEvent>();
        VisibleEvents = Events.Take(MaxVisibleChips).ToList();
        for (var i = 0; i < Chips.Count; i++)
        {
            var visible = i < VisibleEvents.Count;
            Chips[i].Title = visible ? VisibleEvents[i].Title : string.Empty;
            Chips[i].IsVisible = visible;
        }

        MoreEventsCount = Math.Max(0, Events.Count - MaxVisibleChips);
        HasMoreEvents = MoreEventsCount > 0;
        MoreEventsText = HasMoreEvents && !string.IsNullOrWhiteSpace(moreEventsFormat)
            ? string.Format(CultureInfo.CurrentCulture, moreEventsFormat, MoreEventsCount)
            : string.Empty;
    }

    private void UpdateLunarDayText()
    {
        if (!IsLunarVisible || LunarDate is null)
        {
            LunarDayText = string.Empty;
            return;
        }

        // FR-UI-2: the first lunar day shows "1/month" plus the localized leap marker.
        LunarDayText = CalendarText.LunarCell(LunarDate, _leapMarker);
    }
}
