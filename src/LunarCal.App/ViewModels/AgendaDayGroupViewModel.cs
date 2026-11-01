using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing a group of events occurring on a single date in AgendaView (Figma S08, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class AgendaDayGroupViewModel : ObservableObject
{
    private readonly string _lunarFormat;
    private readonly string _leapMarker;

    /// <summary>The Gregorian solar date for this group.</summary>
    public DateOnly Date { get; }

    /// <summary>Calculated Vietnamese lunar date.</summary>
    public LunarDate? LunarDate { get; }

    /// <summary>Whether this group corresponds to local today.</summary>
    public bool IsToday { get; }

    /// <summary>Day of week display string (e.g. "Thứ Hai", "Monday").</summary>
    public string DayOfWeekName { get; }

    /// <summary>Formatted date string (e.g. "19 Tháng 10, 2026").</summary>
    public string DateDisplay { get; }

    /// <summary>Whether lunar dates are visible per global settings (FR-LUNAR-6).</summary>
    [ObservableProperty]
    private bool _isLunarVisible = true;

    /// <summary>Formatted lunar date text (e.g. "10/9 ÂL" or "1/9 ÂL").</summary>
    [ObservableProperty]
    private string _lunarDayText = string.Empty;

    /// <summary>Whether this date is the 1st day of a lunar month (mùng 1 âm).</summary>
    public bool IsFirstLunarDay => (LunarDate?.Day ?? 0) == 1;

    /// <summary>Events occurring on this date that match active filters.</summary>
    public ObservableCollection<AgendaEventItemViewModel> Events { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="AgendaDayGroupViewModel"/>.
    /// </summary>
    public AgendaDayGroupViewModel(
        DateOnly date,
        LunarDate? lunarDate,
        string dayOfWeekName,
        string dateDisplay,
        bool isToday,
        bool isLunarVisible,
        string? lunarFormat = null,
        string? leapMarker = null)
    {
        _lunarFormat = lunarFormat ?? LocalizationService.Instance["lunar.short_suffix"];
        _leapMarker = leapMarker ?? CalendarText.LeapMarker(LocalizationService.Instance);
        Date = date;
        LunarDate = lunarDate;
        DayOfWeekName = dayOfWeekName;
        DateDisplay = dateDisplay;
        IsToday = isToday;
        IsLunarVisible = isLunarVisible;

        UpdateLunarText();
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
    /// Updates lunar day text; mùng 1 includes month and optional leap indicator (FR-UI-2).
    /// </summary>
    public void UpdateLunarText()
    {
        if (!IsLunarVisible || LunarDate is null)
        {
            LunarDayText = string.Empty;
            return;
        }

        // Agenda headers always show day/month (FR-I18N-5: "15/9" + Lunar label); the first day gets the leap marker.
        var ld = LunarDate.Value;
        var dayMonth = string.Create(
            CultureInfo.InvariantCulture,
            $"{ld.Day}/{ld.Month}{(ld.Day == 1 && ld.IsLeapMonth ? _leapMarker : string.Empty)}");
        LunarDayText = string.Format(CultureInfo.CurrentCulture, _lunarFormat, dayMonth);
    }
}
