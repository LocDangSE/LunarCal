using Serilog;
using Serilog.Events;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Configuration extensions for Serilog with redaction and 14-day file rotation (SEC-LOG-1, SEC-LOG-2, ADR-0019).
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>Default output template for Serilog file sink.</summary>
    public const string DefaultOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Default file retention period in days (SEC-LOG-2).</summary>
    public const int DefaultRetainedFileCountLimit = 14;

    /// <summary>Default log directory in %LOCALAPPDATA%/LunarCal/logs.</summary>
    public static string GetDefaultLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LunarCal",
            "logs");
    }

    /// <summary>
    /// Configures Serilog with file rotation (14 days), redaction enricher, destructuring policy, and redacting formatter.
    /// </summary>
    public static LoggerConfiguration ConfigureLunarCalLogging(
        this LoggerConfiguration loggerConfiguration,
        string? logDirectory = null,
        LogEventLevel minimumLevel = LogEventLevel.Information,
        bool redactEmail = false)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);

        var directory = string.IsNullOrWhiteSpace(logDirectory)
            ? GetDefaultLogDirectory()
            : logDirectory;

        var logFilePath = Path.Combine(directory, "lunarcal-.log");
        var formatter = new RedactingTextFormatter(DefaultOutputTemplate, redactEmail: redactEmail);

        return loggerConfiguration
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With(new RedactingEnricher(redactEmail))
            .Destructure.With(new RedactingDestructuringPolicy(redactEmail))
            .WriteTo.File(
                formatter: formatter,
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: DefaultRetainedFileCountLimit,
                shared: true);
    }
}
