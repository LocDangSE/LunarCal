namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Configuration options for wiping local application data (spec §5.3, SEC-DATA-3).
/// </summary>
public sealed class LocalDataWipeOptions
{
    /// <summary>
    /// Root application data directory.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal'.
    /// </summary>
    public string? RootDirectory { get; set; }

    /// <summary>
    /// Explicit SQLite database file path.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal/lunarcal.db' (or relative to <see cref="RootDirectory"/>).
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Directory where DPAPI encrypted secrets are stored.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal/secrets' (or relative to <see cref="RootDirectory"/>).
    /// </summary>
    public string? SecretsDirectory { get; set; }

    /// <summary>
    /// Directory where local data lake outbox files are stored.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal/datalake' (or relative to <see cref="RootDirectory"/>).
    /// </summary>
    public string? DataLakeDirectory { get; set; }
}
