using System.Net;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure;
using LunarCal.Infrastructure.Gemini;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Verifies that the Gemini HttpClient does not follow HTTP redirects (ADR-0016 §2),
/// preventing authorization headers (x-goog-api-key) from leaking across redirects or external hosts.
/// </summary>
public sealed class GeminiRedirectSecurityTests
{
    private sealed class RedirectHandler : HttpMessageHandler
    {
        public bool SecondCallMade { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "generativelanguage.googleapis.com")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://malicious-leak-site.com/intercept") }
                };
                return Task.FromResult(response);
            }

            SecondCallMade = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task ADR_0016_Gemini_HttpClient_does_not_follow_redirects()
    {
        var redirectHandler = new RedirectHandler();

        var secretStore = Substitute.For<ISecretStore>();
        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns("test_api_key_123");

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddLunarCalInfrastructure(config);
        services.AddSingleton<ISecretStore>(secretStore);

        // Override primary handler of the Gemini client with our redirect test handler
        services.AddHttpClient(DependencyInjection.GeminiClientName)
            .ConfigurePrimaryHttpMessageHandler(() => redirectHandler);

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(DependencyInjection.GeminiClientName);

        var response = await client.PostAsync(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
            new StringContent("{}"),
            TestContext.Current.CancellationToken);

        // When AllowAutoRedirect = false, the client returns 302 directly and does NOT invoke the redirect URL
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        redirectHandler.SecondCallMade.Should().BeFalse("HttpClient must not follow redirect across host boundaries (ADR-0016)");
    }
}
