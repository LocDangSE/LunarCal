using System.ComponentModel;

namespace LunarCal.App.Services;

/// <summary>
/// Single in-memory source of the display preferences shared by every calendar view
/// (FR-UI-7, FR-I18N-4, FR-LUNAR-6). Values are loaded from and persisted to <c>ISettingsStore</c>.
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised with the property name whenever a value changes,
/// so views can re-render without each one re-reading the settings store.
/// </summary>
public interface IDisplayPreferences : INotifyPropertyChanged
{
    /// <summary>Time zone used to bucket events into days and to display times.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Time format identifier: <c>"24h"</c> or <c>"12h"</c>.</summary>
    string TimeFormat { get; }

    /// <summary>First day of the week for month, week and mini-calendar grids.</summary>
    DayOfWeek WeekStart { get; }

    /// <summary>Master lunar display toggle (FR-LUNAR-6).</summary>
    bool LunarEnabled { get; }

    /// <summary>Loads all values from the settings store.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets and persists the display time zone (Windows or IANA identifier).</summary>
    Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken = default);

    /// <summary>Sets and persists the time format (<c>"24h"</c> or <c>"12h"</c>).</summary>
    Task SetTimeFormatAsync(string timeFormat, CancellationToken cancellationToken = default);

    /// <summary>Sets and persists the first day of the week.</summary>
    Task SetWeekStartAsync(DayOfWeek weekStart, CancellationToken cancellationToken = default);

    /// <summary>Sets and persists the lunar display toggle.</summary>
    Task SetLunarEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
