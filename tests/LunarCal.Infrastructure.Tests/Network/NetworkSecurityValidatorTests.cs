using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Network;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Network;

public class NetworkSecurityValidatorTests
{
    private readonly NetworkOptions _defaultOptions = new();

    [Fact]
    public void SEC_NET_1_Allows_exact_match_allowed_hosts()
    {
        // accounts.google.com and oauth2.googleapis.com are in default allowed hosts
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "https://accounts.google.com/o/oauth2/v2/auth");
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");

        var act1 = () => NetworkSecurityValidator.ValidateRequest(req1, _defaultOptions);
        var act2 = () => NetworkSecurityValidator.ValidateRequest(req2, _defaultOptions);

        act1();
        act2();
    }

    [Fact]
    public void SEC_NET_1_Allows_wildcard_subdomain_host()
    {
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "https://calendar.googleapis.com/calendar/v3/users/me/calendarList");
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");

        NetworkSecurityValidator.ValidateRequest(req1, _defaultOptions);
        NetworkSecurityValidator.ValidateRequest(req2, _defaultOptions);
    }

    [Fact]
    public void SEC_NET_1_Allows_wildcard_apex_domain()
    {
        // *.googleapis.com pattern should also match googleapis.com apex domain
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://googleapis.com/");

        NetworkSecurityValidator.ValidateRequest(req, _defaultOptions);
    }

    [Fact]
    public void SEC_NET_1_Blocks_host_not_in_allow_list()
    {
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "https://evil.com/api/exfiltrate");
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "https://attacker.googleapis.com.fake.com/");

        var ex1 = Assert.Throws<NetworkSecurityException>(() => NetworkSecurityValidator.ValidateRequest(req1, _defaultOptions));
        Assert.Equal(NetworkSecurityViolationReason.DisallowedHost, ex1.Reason);
        Assert.Equal("evil.com", ex1.BlockedHost);

        var ex2 = Assert.Throws<NetworkSecurityException>(() => NetworkSecurityValidator.ValidateRequest(req2, _defaultOptions));
        Assert.Equal(NetworkSecurityViolationReason.DisallowedHost, ex2.Reason);
        Assert.Equal("attacker.googleapis.com.fake.com", ex2.BlockedHost);
    }

    [Fact]
    public void SEC_NET_1_Blocks_insecure_http_scheme()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "http://accounts.google.com/oauth");

        var ex = Assert.Throws<NetworkSecurityException>(() => NetworkSecurityValidator.ValidateRequest(req, _defaultOptions));
        Assert.Equal(NetworkSecurityViolationReason.InsecureScheme, ex.Reason);
        Assert.Equal("http", ex.BlockedUri?.Scheme);
    }

    [Fact]
    public void SEC_NET_1_Throws_on_null_or_invalid_uri()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        var ex = Assert.Throws<NetworkSecurityException>(() => NetworkSecurityValidator.ValidateRequest(req, _defaultOptions));
        Assert.Equal(NetworkSecurityViolationReason.InvalidUri, ex.Reason);
    }

    [Fact]
    public void SEC_NET_1_Allows_custom_configured_ingestion_host()
    {
        var customOptions = new NetworkOptions
        {
            AllowedHosts = new List<string> { "data.lunarcal.app", "*.googleapis.com" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://data.lunarcal.app/ingest/v1");

        NetworkSecurityValidator.ValidateRequest(req, customOptions);
    }
}
