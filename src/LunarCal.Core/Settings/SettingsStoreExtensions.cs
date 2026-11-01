using LunarCal.Core.Abstractions;

namespace LunarCal.Core.Settings;

/// <summary>
/// Convenience extension methods for strongly-typed access to common settings in <see cref="ISettingsStore"/> (FR-UI-7, FR-LUNAR-6).
/// </summary>
public static class SettingsStoreExtensions
{
    /// <summary>Gets the master lunar calendar toggle setting.</summary>
    public static ValueTask<bool> GetLunarEnabledAsync(
        this ISettingsStore store,
        bool defaultEnabled = AppSettingDefaults.DefaultLunarEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.GetBoolAsync(SettingKeys.LunarEnabled, defaultEnabled, cancellationToken);
    }

    /// <summary>Sets the master lunar calendar toggle setting.</summary>
    public static ValueTask SetLunarEnabledAsync(
        this ISettingsStore store,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SetBoolAsync(SettingKeys.LunarEnabled, enabled, cancellationToken);
    }

    /// <summary>Gets the first day of the week setting.</summary>
    public static async ValueTask<DayOfWeek> GetWeekStartAsync(
        this ISettingsStore store,
        DayOfWeek defaultWeekStart = DayOfWeek.Monday,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var raw = await store.GetStringAsync(SettingKeys.WeekStart, cancellationToken).ConfigureAwait(false);
        if (raw != null && Enum.TryParse<DayOfWeek>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return defaultWeekStart;
    }

    /// <summary>Sets the first day of the week setting.</summary>
    public static ValueTask SetWeekStartAsync(
        this ISettingsStore store,
        DayOfWeek weekStart,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SetStringAsync(SettingKeys.WeekStart, weekStart.ToString(), cancellationToken);
    }

    /// <summary>Gets the UI language code setting (e.g. "vi-VN" or "en-US").</summary>
    public static async ValueTask<string> GetUiLanguageAsync(
        this ISettingsStore store,
        string defaultLanguage = "vi-VN",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var value = await store.GetStringAsync(SettingKeys.UiLanguage, cancellationToken).ConfigureAwait(false);
        return value ?? defaultLanguage;
    }

    /// <summary>Sets the UI language code setting.</summary>
    public static ValueTask SetUiLanguageAsync(
        this ISettingsStore store,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SetStringAsync(SettingKeys.UiLanguage, language, cancellationToken);
    }

    /// <summary>Gets the UI theme setting ("System", "Light", "Dark").</summary>
    public static async ValueTask<string> GetThemeAsync(
        this ISettingsStore store,
        string defaultTheme = AppSettingDefaults.DefaultTheme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var value = await store.GetStringAsync(SettingKeys.Theme, cancellationToken).ConfigureAwait(false);
        return value ?? defaultTheme;
    }

    /// <summary>Sets the UI theme setting.</summary>
    public static ValueTask SetThemeAsync(
        this ISettingsStore store,
        string theme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SetStringAsync(SettingKeys.Theme, theme, cancellationToken);
    }

    /// <summary>Gets the timezone identifier setting.</summary>
    public static async ValueTask<string> GetTimezoneAsync(
        this ISettingsStore store,
        string? defaultTimezone = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var value = await store.GetStringAsync(SettingKeys.Timezone, cancellationToken).ConfigureAwait(false);
        return value ?? defaultTimezone ?? AppSettingDefaults.ResolveDefaultTimezone();
    }

    /// <summary>Sets the timezone identifier setting.</summary>
    public static ValueTask SetTimezoneAsync(
        this ISettingsStore store,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SetStringAsync(SettingKeys.Timezone, timezoneId, cancellationToken);
    }
}
