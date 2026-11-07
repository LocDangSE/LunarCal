using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Configuration options for SQLCipher SQLite local storage (spec §6, SEC-DATA-1, ADR-0013).
/// </summary>
public sealed class SqliteStorageOptions
{
    /// <summary>
    /// File path to the SQLite database.
    /// If null or empty, defaults to '%LOCALAPPDATA%/LunarCal/lunarcal.db'.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Secret store key under which the 256-bit database encryption key is kept.
    /// Defaults to <see cref="KnownSecretKeys.DatabaseKey"/>.
    /// </summary>
    public string SecretKeyName { get; set; } = KnownSecretKeys.DatabaseKey;
}
