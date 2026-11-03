namespace LunarCal.Core.Abstractions;

/// <summary>
/// Result of a calendar synchronization operation (FR-SYNC-1, FR-SYNC-2).
/// </summary>
public sealed record SyncResult
{
    public required bool Success { get; init; }
    public int CalendarsSynced { get; init; }
    public int EventsSynced { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresReauthentication { get; init; }
    public bool NeedsFullResync { get; init; }

    public static SyncResult Succeeded(int calendarsSynced, int eventsSynced) =>
        new()
        {
            Success = true,
            CalendarsSynced = calendarsSynced,
            EventsSynced = eventsSynced
        };

    public static SyncResult Failed(string errorMessage, bool requiresReauth = false, bool needsFullResync = false) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            RequiresReauthentication = requiresReauth,
            NeedsFullResync = needsFullResync
        };
}
