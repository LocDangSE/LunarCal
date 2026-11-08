using AwesomeAssertions;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Security;

/// <summary>
/// Unit tests for <see cref="SecretValueRegistry"/> (ADR-0016, SEC-KEY-1, SEC-KEY-3).
/// Verifies dynamic exact-value matching and redaction across log and exception messages,
/// particularly for API keys not adhering to standard AIza... prefixes.
/// </summary>
public sealed class SecretValueRegistryTests : IDisposable
{
    public SecretValueRegistryTests()
    {
        SecretValueRegistry.Clear();
    }

    public void Dispose()
    {
        SecretValueRegistry.Clear();
    }

    [Fact]
    public void ADR_0016_SecretValueRegistry_redacts_custom_key_not_matching_AIza_pattern()
    {
        // Non-AIza secret key
        var customKey = "secret_custom_token_9876543210_xyz";
        SecretValueRegistry.Register(customKey);

        var logMessage = $"Error occurred while calling Gemini with key {customKey} in session.";
        var sanitized = LogRedactor.Redact(logMessage);

        sanitized.Should().NotContain(customKey);
        sanitized.Should().Contain(LogRedactor.RedactedApiKey);
    }

    [Fact]
    public void ADR_0016_SecretValueRegistry_redacts_url_encoded_variants()
    {
        var complexKey = "key+with/special=chars?123";
        SecretValueRegistry.Register(complexKey);

        var encoded = Uri.EscapeDataString(complexKey);
        var logMessage = $"Request URL: https://example.com/api?token={encoded}";

        var sanitized = LogRedactor.Redact(logMessage);

        sanitized.Should().NotContain(complexKey);
        sanitized.Should().NotContain(encoded);
        sanitized.Should().Contain(LogRedactor.RedactedApiKey);
    }

    [Fact]
    public void ADR_0016_SecretValueRegistry_unregisters_key_cleanly()
    {
        var ephemeralKey = "ephemeral_key_to_be_deleted_12345";
        SecretValueRegistry.Register(ephemeralKey);

        LogRedactor.Redact($"Using {ephemeralKey}").Should().NotContain(ephemeralKey);

        SecretValueRegistry.Unregister(ephemeralKey);

        LogRedactor.Redact($"Using {ephemeralKey}").Should().Contain(ephemeralKey);
    }

    [Fact]
    public void SEC_KEY_3_Exception_messages_do_not_contain_registered_secret_value()
    {
        var secretKey = "super_secret_canary_key_abcdef";
        SecretValueRegistry.Register(secretKey);

        var ex = new GeminiApiException(
            GeminiErrorKind.InvalidApiKey,
            $"Failed with key {secretKey}",
            geminiRawMessage: $"Raw response referencing {secretKey}");

        ex.Message.Should().NotContain(secretKey);
        ex.Message.Should().Contain(LogRedactor.RedactedApiKey);
        ex.GeminiRawMessage.Should().NotContain(secretKey);
        ex.GeminiRawMessage.Should().Contain(LogRedactor.RedactedApiKey);
    }

    [Fact]
    public void ADR_0016_Short_strings_are_ignored_to_prevent_partial_replacements()
    {
        SecretValueRegistry.Register("abc"); // < 4 chars
        SecretValueRegistry.HasSecrets.Should().BeFalse();

        var text = "The alphabet starts with abcde";
        SecretValueRegistry.Redact(text).Should().Be(text);
    }
}
