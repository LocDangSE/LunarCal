namespace LunarCal.Infrastructure.Google;

/// <summary>
/// Configuration options for Google Calendar synchronization and throttling (FR-SYNC-1, FR-SYNC-5).
/// Bound from configuration section "LunarCal:Sync".
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "LunarCal:Sync";

    /// <summary>
    /// Number of months in the past to synchronize events (default: 12).
    /// </summary>
    public int WindowMonthsBack { get; set; } = 12;

    /// <summary>
    /// Number of months in the future to synchronize events (default: 24).
    /// </summary>
    public int WindowMonthsAhead { get; set; } = 24;

    /// <summary>
    /// Polling interval in minutes when the main window is active (default: 2).
    /// </summary>
    public int PollingActiveMinutes { get; set; } = 2;

    /// <summary>
    /// Polling interval in minutes when the application is minimized to the system tray (default: 10).
    /// </summary>
    public int PollingTrayMinutes { get; set; } = 10;

    /// <summary>
    /// Minimum seconds between two syncs triggered by the window gaining focus (default: 30), so switching between
    /// windows quickly does not start a sync every time (FR-SYNC-3, P4-R11).
    /// </summary>
    public int FocusSyncMinIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of requests per second allowed across sync and API operations (FR-SYNC-5, default: 5).
    /// </summary>
    public int MaxRequestsPerSecond { get; set; } = 5;
}
