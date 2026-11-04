using System.Globalization;
using LunarCal.Core.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace LunarCal.App.Services;

/// <summary>
/// Runtime log level for Settings > Advanced (S20, SEC-LOG-1): Information by default; Debug only on request and
/// for at most 24 hours, after which the next start (or the next check) returns to Information.
/// </summary>
public sealed class LogLevelService
{
    /// <summary>AppSetting key holding the UTC instant until which Debug logging stays on.</summary>
    public const string DebugUntilKey = "log_debug_until";

    private static readonly TimeSpan _debugDuration = TimeSpan.FromHours(24);
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _time;

    /// <summary>Creates the service around the Serilog level switch used by the file sink.</summary>
    public LogLevelService(LoggingLevelSwitch levelSwitch, ISettingsStore settings, TimeProvider? time = null)
    {
        LevelSwitch = levelSwitch ?? throw new ArgumentNullException(nameof(levelSwitch));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The switch the logger reads.</summary>
    public LoggingLevelSwitch LevelSwitch { get; }

    /// <summary>Whether Debug logging is on.</summary>
    public bool IsDebug => LevelSwitch.MinimumLevel <= LogEventLevel.Debug;

    /// <summary>Applies the stored choice, dropping Debug once its 24 hours are over.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var until = await _settings.GetStringAsync(DebugUntilKey, cancellationToken).ConfigureAwait(false);
        var debug = DateTimeOffset.TryParse(until, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)
            && end > _time.GetUtcNow();
        if (!debug && until is not null)
        {
            await _settings.SetStringAsync(DebugUntilKey, null, cancellationToken).ConfigureAwait(false);
        }

        LevelSwitch.MinimumLevel = debug ? LogEventLevel.Debug : LogEventLevel.Information;
    }

    /// <summary>Turns Debug logging on for 24 hours, or back to Information.</summary>
    public async Task SetDebugAsync(bool debug, CancellationToken cancellationToken = default)
    {
        var until = debug ? (_time.GetUtcNow() + _debugDuration).ToString("O", CultureInfo.InvariantCulture) : null;
        await _settings.SetStringAsync(DebugUntilKey, until, cancellationToken).ConfigureAwait(false);
        LevelSwitch.MinimumLevel = debug ? LogEventLevel.Debug : LogEventLevel.Information;
    }
}
