using Microsoft.Extensions.Configuration;
using Serilog;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Validates configuration to prevent API keys from being supplied via insecure config files or environment variables (ADR-0016 §3).
/// </summary>
public static class GeminiConfigurationValidator
{
    private static readonly string[] _forbiddenKeyNames = new[]
    {
        "LunarCal:Gemini:ApiKey",
        "Gemini:ApiKey",
        "GEMINI_API_KEY"
    };

    /// <summary>
    /// Checks for misplaced Gemini API keys in configuration sources, logs a warning, and guarantees they are ignored.
    /// </summary>
    public static void WarnIfApiKeyInConfiguration(IConfiguration configuration, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var keyName in _forbiddenKeyNames)
        {
            var value = configuration[keyName];
            if (!string.IsNullOrWhiteSpace(value))
            {
                logger?.Warning(
                    "Detected Gemini API key in configuration key '{ConfigKey}'. Ignoring it for security; API keys are only loaded from ISecretStore (SEC-KEY-1, ADR-0016).",
                    keyName);
            }
        }
    }
}
