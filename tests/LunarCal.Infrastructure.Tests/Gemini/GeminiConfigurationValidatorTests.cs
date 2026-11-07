using AwesomeAssertions;
using LunarCal.Infrastructure.Gemini;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Serilog;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiConfigurationValidator"/> (ADR-0016 §3).
/// Verifies warnings are logged and secrets in configuration files or env variables are ignored.
/// </summary>
public sealed class GeminiConfigurationValidatorTests
{
    [Theory]
    [InlineData("LunarCal:Gemini:ApiKey")]
    [InlineData("Gemini:ApiKey")]
    [InlineData("GEMINI_API_KEY")]
    public void ADR_0016_Warns_and_ignores_when_key_is_in_configuration(string configKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configKey] = "AIzaSyInsecureKeyInConfig12345"
            })
            .Build();

        var logger = Substitute.For<ILogger>();

        GeminiConfigurationValidator.WarnIfApiKeyInConfiguration(config, logger);

        var calls = logger.ReceivedCalls().ToList();
        calls.Should().ContainSingle();
        var call = calls[0];
        call.GetMethodInfo().Name.Should().Be("Warning");
        var args = call.GetArguments();
        args[0]?.ToString().Should().Contain("Ignoring it for security");
        args[0]?.ToString().Should().Contain("SEC-KEY-1");
        args[1]?.ToString().Should().Be(configKey);
    }

    [Fact]
    public void ADR_0016_No_warning_when_key_is_absent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LunarCal:Gemini:DefaultModelId"] = "gemini-3.8-flash"
            })
            .Build();

        var logger = Substitute.For<ILogger>();

        GeminiConfigurationValidator.WarnIfApiKeyInConfiguration(config, logger);

        logger.ReceivedCalls().Should().BeEmpty();
    }
}
