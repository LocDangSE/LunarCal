using AwesomeAssertions;
using LunarCal.App.Services;
using LunarCal.Infrastructure.Storage;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace LunarCal.App.Tests.Services;

/// <summary>
/// P3-REVIEW (P3-R3): the log level in Settings > Advanced (Figma S20) is Information by default and Debug only for
/// 24 hours (SEC-LOG-1).
/// </summary>
public sealed class LogLevelServiceTests
{
    [Fact]
    public async Task SEC_LOG_1_Debug_survives_a_restart_within_24_hours_and_expires_after()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new InMemorySettingsStore();
        var clock = new ManualTime(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var first = new LogLevelService(new LoggingLevelSwitch(LogEventLevel.Information), settings, clock);

        await first.SetDebugAsync(true, ct);
        first.IsDebug.Should().BeTrue();

        clock.Now = clock.Now.AddHours(23);
        var restarted = new LogLevelService(new LoggingLevelSwitch(LogEventLevel.Information), settings, clock);
        await restarted.LoadAsync(ct);
        restarted.IsDebug.Should().BeTrue();

        clock.Now = clock.Now.AddHours(2);
        var expired = new LogLevelService(new LoggingLevelSwitch(LogEventLevel.Debug), settings, clock);
        await expired.LoadAsync(ct);
        expired.IsDebug.Should().BeFalse();
        expired.LevelSwitch.MinimumLevel.Should().Be(LogEventLevel.Information);
        (await settings.GetStringAsync(LogLevelService.DebugUntilKey, ct)).Should().BeNull();
    }

    [Fact]
    public async Task SEC_LOG_1_Default_level_is_information()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new LogLevelService(new LoggingLevelSwitch(LogEventLevel.Debug), new InMemorySettingsStore());

        await service.LoadAsync(ct);

        service.IsDebug.Should().BeFalse();
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
