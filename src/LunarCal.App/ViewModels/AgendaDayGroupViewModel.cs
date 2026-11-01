using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.Core.Lunar;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model representing a group of events occurring on a single date in AgendaView (Figma S08, FR-UI-1, FR-LUNAR-6).
/// </summary>
public sealed partial class AgendaDayGroupViewModel : ObservableObject
{
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
        bool isLunarVisible)
    {
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

        var ld = LunarDate.Value;
        if (ld.Day == 1)
        {
            LunarDayText = $"1/{ld.Month}{(ld.IsLeapMonth ? "n" : string.Empty)} ÂL";
        }
        else
        {
            LunarDayText = $"{ld.Day}/{ld.Month} ÂL";
        }
    }
}
