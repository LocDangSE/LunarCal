using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using LunarCal.Infrastructure.Google.OAuth;
using LunarCal.Infrastructure.Storage;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Google;

/// <summary>
/// P3-REVIEW (P3-R4): the OAuth client typed in Settings > Advanced is actually used (FR-AUTH-6) and its secret is
/// kept only in the DPAPI secret store (SEC-KEY-1). Before the fix both were written to AppSetting and never read.
/// </summary>
public sealed class OAuthClientOverrideServiceTests
{
    private static (OAuthClientOverrideService Service, GoogleOAuthOptions Options, InMemorySettingsStore Settings, ISecretStore Secrets, Dictionary<string, string> Vault) Create()
    {
        var options = new GoogleOAuthOptions { ClientId = "build-client", ClientSecret = "build-secret" };
        var settings = new InMemorySettingsStore();
        var secrets = new FakeSecretStore();
        var vault = secrets.Vault;
        return (new OAuthClientOverrideService(options, settings, secrets), options, settings, secrets, vault);
    }

    [Fact]
    public async Task FR_AUTH_6_Saved_client_is_used_and_its_secret_never_reaches_AppSetting()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, options, settings, _, vault) = Create();

        await service.SaveAsync(" mine.apps.googleusercontent.com ", "my-secret", ct);

        options.ClientId.Should().Be("mine.apps.googleusercontent.com");
        options.ClientSecret.Should().Be("my-secret");
        vault[KnownSecretKeys.GoogleOAuthClientSecretOverride].Should().Be("my-secret");
        (await settings.GetAllAsync(ct)).Values.Should().NotContain(v => v.Contains("my-secret"));
        service.CustomClientId.Should().Be("mine.apps.googleusercontent.com");
        service.HasCustomClientSecret.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_6_Reset_returns_to_the_build_client_and_deletes_the_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, options, settings, _, vault) = Create();
        await service.SaveAsync("mine", "my-secret", ct);

        await service.ResetAsync(ct);

        options.ClientId.Should().Be("build-client");
        options.ClientSecret.Should().Be("build-secret");
        vault.Should().BeEmpty();
        (await settings.GetStringAsync(SettingKeys.GoogleOAuthClientId, ct)).Should().BeNull();
    }

    [Fact]
    public async Task SEC_KEY_1_Load_moves_a_secret_stored_by_older_builds_out_of_AppSetting()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, options, settings, _, vault) = Create();
        await settings.SetStringAsync(SettingKeys.GoogleOAuthClientId, "legacy-client", ct);
        await settings.SetStringAsync(SettingKeys.GoogleOAuthClientSecret, "legacy-secret", ct);

        await service.LoadAsync(ct);

        (await settings.GetStringAsync(SettingKeys.GoogleOAuthClientSecret, ct)).Should().BeNull();
        vault[KnownSecretKeys.GoogleOAuthClientSecretOverride].Should().Be("legacy-secret");
        options.ClientId.Should().Be("legacy-client");
        options.ClientSecret.Should().Be("legacy-secret");
    }

    [Fact]
    public async Task FR_AUTH_6_Saved_client_id_is_sent_in_the_authorize_url_and_reset_restores_the_build_client()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, options, _, secrets, _) = Create();
        var listener = Substitute.For<ILoopbackHttpListener>();
        listener.RedirectUri.Returns("http://127.0.0.1:54321/");
        listener.WaitForCallbackAsync(Arg.Any<CancellationToken>())
            .Returns(new LoopbackCallbackResult { Error = "access_denied" });
        var listeners = Substitute.For<ILoopbackHttpListenerFactory>();
        listeners.CreateListener(Arg.Any<TimeSpan>(), Arg.Any<int>()).Returns(listener);
        var opened = new List<Uri>();
        var browser = Substitute.For<ISystemBrowser>();
        browser.OpenBrowserAsync(Arg.Do<Uri>(opened.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var client = new GoogleOAuthClient(options, secrets, listeners, browser);

        await service.SaveAsync("mine.apps.googleusercontent.com", "my-secret", ct);
        await client.AuthorizeAsync(ct);
        await service.ResetAsync(ct);
        await client.AuthorizeAsync(ct);

        opened.Should().HaveCount(2);
        opened[0].Query.Should().Contain("client_id=mine.apps.googleusercontent.com");
        opened[1].Query.Should().Contain("client_id=build-client");
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public Dictionary<string, string> Vault { get; } = new(StringComparer.Ordinal);

        public ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Vault.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            Vault[key] = secret;
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetSecretBytesAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetSecretBytesAsync(string key, byte[] secret, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Vault.ContainsKey(key));

        public ValueTask<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Vault.Remove(key));

        public ValueTask ClearAllAsync(CancellationToken cancellationToken = default)
        {
            Vault.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
