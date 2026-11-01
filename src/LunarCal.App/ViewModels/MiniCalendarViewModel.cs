using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LunarCal.App.ViewModels;

/// <summary>
/// View model controlling the sidebar mini calendar navigation and selection (FR-UI-1, FR-UI-10).
/// </summary>
public sealed partial class MiniCalendarViewModel : ObservableObject
{
    [ObservableProperty]
    private DateOnly _displayMonth;

    [ObservableProperty]
    private DateOnly _selectedDate;

    [ObservableProperty]
    private string _monthTitle = string.Empty;

    private DayOfWeek _weekStart = DayOfWeek.Monday;

    /// <summary>
    /// Event raised when a date is selected in the mini calendar.
    /// </summary>
    public event EventHandler<DateOnly>? DateSelected;

    /// <summary>Day of week abbreviated header labels (e.g. T2, T3... or Mon, Tue...).</summary>
    public ObservableCollection<string> DayHeaders { get; } = new();

    /// <summary>The 42 date cells in the 6×7 calendar grid.</summary>
    public ObservableCollection<MiniCalendarDayViewModel> Days { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="MiniCalendarViewModel"/>.
    /// </summary>
    public MiniCalendarViewModel()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _selectedDate = today;
        _displayMonth = new DateOnly(today.Year, today.Month, 1);
        RebuildHeaders();
        RebuildGrid();
    }

    /// <summary>
    /// Updates the week start day and culture, rebuilding headers and grid accordingly.
    /// </summary>
    public void SetCultureAndWeekStart(CultureInfo culture, DayOfWeek weekStart)
    {
        _weekStart = weekStart;
        RebuildHeaders(culture);
        RebuildGrid();
    }

    /// <summary>
    /// Sets the selected date and moves the display month if necessary.
    /// </summary>
    public void SetSelectedDate(DateOnly date)
    {
        SelectedDate = date;
        if (DisplayMonth.Year != date.Year || DisplayMonth.Month != date.Month)
        {
            DisplayMonth = new DateOnly(date.Year, date.Month, 1);
        }

        RebuildGrid();
    }

    [RelayCommand]
    public void PreviousMonth()
    {
        DisplayMonth = DisplayMonth.AddMonths(-1);
        RebuildGrid();
    }

    [RelayCommand]
    public void NextMonth()
    {
        DisplayMonth = DisplayMonth.AddMonths(1);
        RebuildGrid();
    }

    [RelayCommand]
    public void SelectDay(MiniCalendarDayViewModel day)
    {
        ArgumentNullException.ThrowIfNull(day);

        SetSelectedDate(day.Date);
        DateSelected?.Invoke(this, day.Date);
    }

    private void RebuildHeaders(CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentUICulture;
        DayHeaders.Clear();

        var day = _weekStart;
        for (int i = 0; i < 7; i++)
        {
            var header = targetCulture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                ? day switch
                {
                    DayOfWeek.Monday => "T2",
                    DayOfWeek.Tuesday => "T3",
                    DayOfWeek.Wednesday => "T4",
                    DayOfWeek.Thursday => "T5",
                    DayOfWeek.Friday => "T6",
                    DayOfWeek.Saturday => "T7",
                    DayOfWeek.Sunday => "CN",
                    _ => string.Empty,
                }
                : targetCulture.DateTimeFormat.GetAbbreviatedDayName(day).Substring(0, Math.Min(2, targetCulture.DateTimeFormat.GetAbbreviatedDayName(day).Length));

            DayHeaders.Add(header);
            day = (DayOfWeek)(((int)day + 1) % 7);
        }
    }

    private void RebuildGrid()
    {
        var culture = CultureInfo.CurrentUICulture;
        MonthTitle = culture.Name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
            ? $"Tháng {DisplayMonth.Month}, {DisplayMonth.Year}"
            : DisplayMonth.ToString("MMMM yyyy", culture);

        var firstDayOfMonth = DisplayMonth;
        int diff = ((int)firstDayOfMonth.DayOfWeek - (int)_weekStart + 7) % 7;
        var startGridDate = firstDayOfMonth.AddDays(-diff);

        var today = DateOnly.FromDateTime(DateTime.Today);

        Days.Clear();
        var cursor = startGridDate;
        for (int i = 0; i < 42; i++)
        {
            Days.Add(new MiniCalendarDayViewModel
            {
                Date = cursor,
                DayNumber = cursor.Day,
                IsCurrentMonth = cursor.Month == DisplayMonth.Month,
                IsToday = cursor == today,
                IsSelected = cursor == SelectedDate,
            });

            cursor = cursor.AddDays(1);
        }
    }
}
