namespace LunarCal.Core.Abstractions;

/// <summary>
/// Port for reading and persisting key-value application settings (spec §6 AppSetting, FR-UI-7, FR-LUNAR-6).
/// Phase 2 uses an in-memory implementation; Phase 3 transitions to SQLCipher SQLite storage.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Gets a setting value by key, or null if not set.</summary>
    ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Sets a setting value by key. Passing null removes the setting.</summary>
    ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Gets all current key-value settings.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a boolean setting value, or returns the provided default if unset or unparsable.</summary>
    ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false, CancellationToken cancellationToken = default);

    /// <summary>Sets a boolean setting value ("true" or "false").</summary>
    ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default);
}
