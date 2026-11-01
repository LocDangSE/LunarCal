using System.Collections.Concurrent;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ISettingsStore"/> for Phase 2.
/// Pre-seeded with application defaults (FR-UI-7, FR-LUNAR-6, spec §6).
/// Phase 3 introduces SQLite/SQLCipher persistent storage.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string> _settings;

    /// <summary>
    /// Initializes a new in-memory settings store seeded with initial application defaults.
    /// </summary>
    public InMemorySettingsStore()
        : this(AppSettingDefaults.CreateInitialDefaults())
    {
    }

    /// <summary>
    /// Initializes a new in-memory settings store seeded with specified settings.
    /// </summary>
    public InMemorySettingsStore(IDictionary<string, string>? initialSettings)
    {
        _settings = initialSettings != null
            ? new ConcurrentDictionary<string, string>(initialSettings, StringComparer.Ordinal)
            : new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _settings.TryGetValue(key, out var value);
        return ValueTask.FromResult<string?>(value);
    }

    /// <inheritdoc />
    public ValueTask SetStringAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (value is null)
        {
            _settings.TryRemove(key, out _);
        }
        else
        {
            _settings[key] = value;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var copy = new Dictionary<string, string>(_settings, StringComparer.Ordinal);
        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(copy);
    }

    /// <inheritdoc />
    public ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
        {
            return ValueTask.FromResult(parsed);
        }

        return ValueTask.FromResult(defaultValue);
    }

    /// <inheritdoc />
    public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _settings[key] = value ? "true" : "false";
        return ValueTask.CompletedTask;
    }
}
