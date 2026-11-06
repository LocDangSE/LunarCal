using AwesomeAssertions;
using LunarCal.Infrastructure.Google.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LunarCal.Infrastructure.Tests.Google;

/// <summary>
/// Regression: appsettings.json ships an empty <c>LunarCal:Google:OAuthClientId</c>, and the `??` fallback chain stopped
/// at that empty string, so the Client ID/secret kept in user-secrets as <c>Google:ClientId</c>/<c>Google:ClientSecret</c>
/// were never used and sign-in failed with <c>missing_client_id</c> (FR-AUTH-6, SEC-KEY-5).
/// </summary>
public sealed class GoogleOAuthConfigurationTests
{
    private static GoogleOAuthOptions Resolve(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLunarCalInfrastructure(configuration)
            .BuildServiceProvider()
            .GetRequiredService<GoogleOAuthOptions>();
    }

    [Fact]
    public void FR_AUTH_6_SEC_KEY_5_Empty_primary_key_falls_through_to_user_secrets_keys()
    {
        var options = Resolve(new()
        {
            ["LunarCal:Google:OAuthClientId"] = "",
            ["Google:ClientId"] = "id-from-user-secrets",
            ["Google:ClientSecret"] = "secret-from-user-secrets",
        });

        options.ClientId.Should().Be("id-from-user-secrets");
        options.ClientSecret.Should().Be("secret-from-user-secrets");
    }

    [Fact]
    public void FR_AUTH_6_Primary_key_wins_when_it_has_a_value()
    {
        var options = Resolve(new()
        {
            ["LunarCal:Google:OAuthClientId"] = "primary-id",
            ["Google:ClientId"] = "fallback-id",
        });

        options.ClientId.Should().Be("primary-id");
    }

    [Fact]
    public void FR_AUTH_6_No_client_configured_leaves_client_id_empty()
    {
        var options = Resolve(new() { ["LunarCal:Google:OAuthClientId"] = "  " });

        options.ClientId.Should().BeEmpty();
        options.ClientSecret.Should().BeNull();
    }
}
