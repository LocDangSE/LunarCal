using LunarCal.Infrastructure.Security;

namespace LunarCal.Infrastructure.Tests.Security;

public class LogRedactorTests
{
    private static readonly string _fakeApiKey = "AIza" + "SyD1234567890123456789012345678901";
    private static readonly string _fakeAccessToken = "ya29" + ".a0AfH6SMD_EXAMPLE_ACCESS_TOKEN_12345";

    [Fact]
    public void SEC_LOG_1_Redacts_api_key_access_token_bearer_refresh_token()
    {
        // Composite text containing all required secret types (SEC-LOG-1)
        var input = $"Request with key={_fakeApiKey}, " +
                    $"token: {_fakeAccessToken}, " +
                    "Authorization: Bearer secret-bearer-token-val-999, " +
                    "and refresh_token=1//0gABC-123_xyz456REFRESH";

        var redacted = LogRedactor.Redact(input);

        redacted.Should().NotContain(_fakeApiKey);
        redacted.Should().NotContain(_fakeAccessToken);
        redacted.Should().NotContain("secret-bearer-token-val-999");
        redacted.Should().NotContain("1//0gABC-123_xyz456REFRESH");

        redacted.Should().Contain(LogRedactor.RedactedApiKey);
        redacted.Should().Contain(LogRedactor.RedactedAccessToken);
        redacted.Should().Contain(LogRedactor.RedactedBearerToken);
        redacted.Should().Contain(LogRedactor.RedactedRefreshToken);
    }

    [Fact]
    public void Redact_GeminiApiKey_IsMasked()
    {
        var rawKey = _fakeApiKey;
        var query = $"key={_fakeApiKey}&alt=json";
        var json = $"{{\"apiKey\":\"{_fakeApiKey}\"}}";

        LogRedactor.Redact(rawKey).Should().Be(LogRedactor.RedactedApiKey);
        LogRedactor.Redact(query).Should().NotContain(_fakeApiKey).And.Contain(LogRedactor.RedactedApiKey);
        LogRedactor.Redact(json).Should().NotContain(_fakeApiKey).And.Contain(LogRedactor.RedactedApiKey);
    }

    [Fact]
    public void Redact_GoogleAccessToken_IsMasked()
    {
        var rawToken = _fakeAccessToken;
        var logLine = $"access_token: {_fakeAccessToken} expired";

        LogRedactor.Redact(rawToken).Should().Be(LogRedactor.RedactedAccessToken);
        LogRedactor.Redact(logLine).Should().NotContain(_fakeAccessToken).And.Contain(LogRedactor.RedactedAccessToken);
    }

    [Theory]
    [InlineData("Bearer my-oauth-access-token-12345")]
    [InlineData("bearer secret_token_xyz")]
    [InlineData("Authorization: Bearer secret_token_xyz")]
    [InlineData("authorization: basic dXNlcjpwYXNz")]
    public void Redact_BearerAndAuthorizationTokens_AreMasked(string input)
    {
        var result = LogRedactor.Redact(input);

        result.Should().NotContain("my-oauth-access-token-12345");
        result.Should().NotContain("secret_token_xyz");
        result.Should().NotContain("dXNlcjpwYXNz");
        result.Should().Contain(LogRedactor.RedactedBearerToken);
    }

    [Theory]
    [InlineData("1//0gAbcDef123456-XYZ_789")]
    [InlineData("refresh_token=1//0gAbcDef123456-XYZ_789&client_id=123")]
    [InlineData("{\"refresh_token\": \"custom_token_abc123\"}")]
    [InlineData("refreshToken: custom_token_abc123;")]
    public void Redact_RefreshToken_IsMasked(string input)
    {
        var result = LogRedactor.Redact(input);

        result.Should().NotContain("1//0gAbcDef123456-XYZ_789");
        result.Should().NotContain("custom_token_abc123");
        result.Should().Contain(LogRedactor.RedactedRefreshToken);
    }

    [Theory]
    [InlineData("GOCSPX-abc123def456ghi789")]
    [InlineData("client_secret=GOCSPX-abc123def456ghi789")]
    [InlineData("{\"client_secret\": \"custom-secret-key-999\"}")]
    [InlineData("clientSecret: custom-secret-key-999")]
    public void Redact_ClientSecret_IsMasked(string input)
    {
        var result = LogRedactor.Redact(input);

        result.Should().NotContain("GOCSPX-abc123def456ghi789");
        result.Should().NotContain("custom-secret-key-999");
        result.Should().Contain(LogRedactor.RedactedClientSecret);
    }

    [Fact]
    public void Redact_EmailAddress_RedactedWhenOptionEnabled()
    {
        var input = "Contact user test.user+cal@example.com for help";

        var withRedaction = LogRedactor.Redact(input, redactEmail: true);
        var withoutRedaction = LogRedactor.Redact(input, redactEmail: false);

        withRedaction.Should().NotContain("test.user+cal@example.com");
        withRedaction.Should().Contain(LogRedactor.RedactedEmail);

        withoutRedaction.Should().Contain("test.user+cal@example.com");
    }

    [Fact]
    public void Redact_NormalLogMessage_RemainsUnchanged()
    {
        var input = "Rendered month view in 24 ms with 42 cells and 15 events.";

        var result = LogRedactor.Redact(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsSame()
    {
        LogRedactor.Redact(null).Should().BeEmpty();
        LogRedactor.Redact(string.Empty).Should().BeEmpty();
    }
}
