using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.App.Services;

namespace LunarCal.App.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IDisplayPreferences"/> for view model tests. Defaults to a fixed UTC+7 zone so test
/// expectations do not depend on the time zone of the machine running the tests.
/// </summary>
internal sealed class TestDisplayPreferences : ObservableObject, IDisplayPreferences
{
    /// <summary>Fixed UTC+07:00 zone without daylight saving (same offset as Asia/Ho_Chi_Minh).</summary>
    public static readonly TimeZoneInfo UtcPlus7 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");

    private TimeZoneInfo _timeZone = UtcPlus7;
    private string _timeFormat = TimeFormattingHelper.Format24Hours;
    private DayOfWeek _weekStart = DayOfWeek.Monday;
    private bool _lunarEnabled = true;

    public TimeZoneInfo TimeZone
    {
        get => _timeZone;
        set => SetProperty(ref _timeZone, value);
    }

    public string TimeFormat
    {
        get => _timeFormat;
        set => SetProperty(ref _timeFormat, value);
    }

    public DayOfWeek WeekStart
    {
        get => _weekStart;
        set => SetProperty(ref _weekStart, value);
    }

    public bool LunarEnabled
    {
        get => _lunarEnabled;
        set => SetProperty(ref _lunarEnabled, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return Task.CompletedTask;
    }

    public Task SetTimeFormatAsync(string timeFormat, CancellationToken cancellationToken = default)
    {
        TimeFormat = timeFormat;
        return Task.CompletedTask;
    }

    public Task SetWeekStartAsync(DayOfWeek weekStart, CancellationToken cancellationToken = default)
    {
        WeekStart = weekStart;
        return Task.CompletedTask;
    }

    public Task SetLunarEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        LunarEnabled = enabled;
        return Task.CompletedTask;
    }
}
