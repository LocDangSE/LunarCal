using System.IO;
using LunarCal.Infrastructure.Security;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace LunarCal.Infrastructure.Tests.Security;

public class SerilogRedactionTests : IDisposable
{
    private static readonly string _fakeApiKey = "AIza" + "SyD1234567890123456789012345678901";
    private static readonly string _fakeAccessToken = "ya29" + ".a0AfH6SMD_ACCESS_TOKEN_XYZ";

    private readonly string _testLogDirectory;

    public SerilogRedactionTests()
    {
        _testLogDirectory = Path.Combine(Path.GetTempPath(), "LunarCal_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testLogDirectory);
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
        try
        {
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SEC_LOG_1_Serilog_redacts_secrets_in_structured_properties_and_templates()
    {
        var logger = new LoggerConfiguration()
            .ConfigureLunarCalLogging(_testLogDirectory, minimumLevel: LogEventLevel.Information)
            .CreateLogger();

        logger.Information("Connecting with ApiKey: {ApiKey} and token: {AccessToken}",
            _fakeApiKey,
            _fakeAccessToken);

        logger.Information("Raw bearer line: Authorization: Bearer top-secret-oauth-bearer-token");
        logger.Information("Refresh token payload: refresh_token=1//0gABC-123_SECRET_REFRESH");

        logger.Dispose();
        Log.CloseAndFlush();

        var logFiles = Directory.GetFiles(_testLogDirectory, "lunarcal-*.log");
        logFiles.Should().NotBeEmpty();

        var content = File.ReadAllText(logFiles[0]);

        content.Should().NotContain(_fakeApiKey);
        content.Should().NotContain(_fakeAccessToken);
        content.Should().NotContain("top-secret-oauth-bearer-token");
        content.Should().NotContain("1//0gABC-123_SECRET_REFRESH");

        content.Should().Contain(LogRedactor.RedactedEventField);
        content.Should().Contain(LogRedactor.RedactedBearerToken);
        content.Should().Contain(LogRedactor.RedactedRefreshToken);
    }

    [Fact]
    public void SEC_LOG_1_Serilog_redacts_sensitive_event_fields_FR_NOTIFY_9()
    {
        var sampleEvent = new TestCalendarEventDto
        {
            Title = "Doctor appointment at clinic",
            Location = "123 Healthcare Ave, Medical Suite 400",
            Description = "Private medical details regarding prescription",
            Summary = "Annual physical checkup"
        };

        var logger = new LoggerConfiguration()
            .ConfigureLunarCalLogging(_testLogDirectory, minimumLevel: LogEventLevel.Information)
            .CreateLogger();

        logger.Information("Syncing calendar event: {@Event}", sampleEvent);

        logger.Dispose();
        Log.CloseAndFlush();

        var logFiles = Directory.GetFiles(_testLogDirectory, "lunarcal-*.log");
        var content = File.ReadAllText(logFiles[0]);

        content.Should().NotContain("Doctor appointment at clinic");
        content.Should().NotContain("123 Healthcare Ave, Medical Suite 400");
        content.Should().NotContain("Private medical details regarding prescription");
        content.Should().NotContain("Annual physical checkup");

        content.Should().Contain(LogRedactor.RedactedEventField);
    }

    [Fact]
    public void SEC_LOG_2_Serilog_defaults_to_information_level_and_suppresses_debug()
    {
        var logger = new LoggerConfiguration()
            .ConfigureLunarCalLogging(_testLogDirectory, minimumLevel: LogEventLevel.Information)
            .CreateLogger();

        logger.Debug("SECRET_DEBUG_MESSAGE_THAT_SHOULD_BE_SUPPRESSED");
        logger.Information("PUBLIC_INFO_MESSAGE");

        logger.Dispose();
        Log.CloseAndFlush();

        var logFiles = Directory.GetFiles(_testLogDirectory, "lunarcal-*.log");
        var content = File.ReadAllText(logFiles[0]);

        content.Should().NotContain("SECRET_DEBUG_MESSAGE_THAT_SHOULD_BE_SUPPRESSED");
        content.Should().Contain("PUBLIC_INFO_MESSAGE");
    }

    [Fact]
    public void RedactingTextFormatter_Sanitizes_Template_And_Exception_Messages()
    {
        var formatter = new RedactingTextFormatter(SerilogConfiguration.DefaultOutputTemplate);

        var ex = new InvalidOperationException($"Failed to call API with key {_fakeApiKey} and Bearer secret-auth-token");
        var logEvent = new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Error,
            ex,
            new MessageTemplateParser().Parse($"Direct secret in template: {_fakeAccessToken}"),
            []);

        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);

        var output = writer.ToString();

        output.Should().NotContain(_fakeApiKey);
        output.Should().NotContain("secret-auth-token");
        output.Should().NotContain(_fakeAccessToken);

        output.Should().Contain(LogRedactor.RedactedApiKey);
        output.Should().Contain(LogRedactor.RedactedBearerToken);
        output.Should().Contain(LogRedactor.RedactedAccessToken);
    }

    [Fact]
    public void RedactingDestructuringPolicy_Sanitizes_Credentials_Object()
    {
        var creds = new TestCredentialsDto
        {
            ApiKey = _fakeApiKey,
            AccessToken = _fakeAccessToken,
            RefreshToken = "1//0gABC-SECRET-REFRESH",
            NonSecretName = "DevelopmentProfile"
        };

        var policy = new RedactingDestructuringPolicy();
        var factory = new TestPropertyValueFactory();

        var success = policy.TryDestructure(creds, factory, out var result);

        success.Should().BeTrue();
        result.Should().BeOfType<StructureValue>();

        var structure = (StructureValue)result!;
        var apiKeyProp = structure.Properties.First(p => p.Name == "ApiKey");
        var nonSecretProp = structure.Properties.First(p => p.Name == "NonSecretName");

        apiKeyProp.Value.ToString().Should().Contain(LogRedactor.RedactedEventField);
        nonSecretProp.Value.ToString().Should().Contain("DevelopmentProfile");
    }

    private sealed class TestCalendarEventDto
    {
        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class TestCredentialsDto
    {
        public string ApiKey { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string NonSecretName { get; set; } = string.Empty;
    }

    private sealed class TestPropertyValueFactory : Serilog.Core.ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
        {
            if (value is null)
            {
                return new ScalarValue(null);
            }

            if (value is string s)
            {
                return new ScalarValue(s);
            }

            return new ScalarValue(value.ToString());
        }
    }
}
