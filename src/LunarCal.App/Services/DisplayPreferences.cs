using CommunityToolkit.Mvvm.ComponentModel;
using LunarCal.App.Localization;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Settings;

namespace LunarCal.App.Services;

/// <summary>
/// Default <see cref="IDisplayPreferences"/> backed by <see cref="ISettingsStore"/> (FR-UI-7, FR-I18N-4, FR-LUNAR-6).
/// </summary>
public sealed partial class DisplayPreferences : ObservableObject, IDisplayPreferences
{
    private readonly ISettingsStore _settingsStore;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private TimeZoneInfo _timeZone = TimeZoneInfo.Local;

    [ObservableProperty]
    private string _timeFormat;

    [ObservableProperty]
    private DayOfWeek _weekStart;

    [ObservableProperty]
    private bool _lunarEnabled = AppSettingDefaults.DefaultLunarEnabled;

    /// <summary>Initializes a new instance with culture-based defaults until <see cref="LoadAsync"/> runs.</summary>
    public DisplayPreferences(ISettingsStore settingsStore, ILocalizationService localizationService)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        var cultureName = _localizationService.CurrentCulture.Name;
        _timeFormat = AppSettingDefaults.ResolveDefaultTimeFormat(cultureName);
        _weekStart = AppSettingDefaults.ResolveDefaultWeekStart(cultureName);
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var cultureName = _localizationService.CurrentCulture.Name;

        var timeZoneId = await _settingsStore.GetTimezoneAsync(
            AppSettingDefaults.ResolveDefaultTimezone(), cancellationToken).ConfigureAwait(true);
        TimeZone = CalendarTime.ResolveTimeZone(timeZoneId, TimeZoneInfo.Local);

        TimeFormat = NormalizeTimeFormat(await _settingsStore.GetTimeFormatAsync(
            AppSettingDefaults.ResolveDefaultTimeFormat(cultureName), cancellationToken).ConfigureAwait(true));

        WeekStart = await _settingsStore.GetWeekStartAsync(
            AppSettingDefaults.ResolveDefaultWeekStart(cultureName), cancellationToken).ConfigureAwait(true);

        LunarEnabled = await _settingsStore.GetLunarEnabledAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var zone = CalendarTime.ResolveTimeZone(timeZoneId, TimeZoneInfo.Local);
        await _settingsStore.SetTimezoneAsync(zone.Id, cancellationToken).ConfigureAwait(true);
        TimeZone = zone;
    }

    /// <inheritdoc />
    public async Task SetTimeFormatAsync(string timeFormat, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTimeFormat(timeFormat);
        await _settingsStore.SetTimeFormatAsync(normalized, cancellationToken).ConfigureAwait(true);
        TimeFormat = normalized;
    }

    /// <inheritdoc />
    public async Task SetWeekStartAsync(DayOfWeek weekStart, CancellationToken cancellationToken = default)
    {
        await _settingsStore.SetWeekStartAsync(weekStart, cancellationToken).ConfigureAwait(true);
        WeekStart = weekStart;
    }

    /// <inheritdoc />
    public async Task SetLunarEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _settingsStore.SetLunarEnabledAsync(enabled, cancellationToken).ConfigureAwait(true);
        LunarEnabled = enabled;
    }

    private static string NormalizeTimeFormat(string? timeFormat) =>
        string.Equals(timeFormat, TimeFormattingHelper.Format12Hours, StringComparison.OrdinalIgnoreCase)
            ? TimeFormattingHelper.Format12Hours
            : TimeFormattingHelper.Format24Hours;
}
