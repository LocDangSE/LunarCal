using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google.OAuth;
using NSubstitute;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class GoogleOAuthClientTests
{
    private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
    private readonly ILoopbackHttpListenerFactory _listenerFactory = Substitute.For<ILoopbackHttpListenerFactory>();
    private readonly ILoopbackHttpListener _listener = Substitute.For<ILoopbackHttpListener>();
    private readonly ISystemBrowser _browser = Substitute.For<ISystemBrowser>();

    public GoogleOAuthClientTests()
    {
        _listener.RedirectUri.Returns("http://127.0.0.1:54321/");
        _listener.Port.Returns(54321);
        _listenerFactory.CreateListener(Arg.Any<TimeSpan>(), Arg.Any<int>()).Returns(_listener);
    }

    [Fact]
    public async Task FR_AUTH_6_Authorize_fails_when_client_id_is_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions { ClientId = "" };
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var result = await client.AuthorizeAsync(ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("missing_client_id");
    }

    [Fact]
    public void FR_AUTH_2_SEC_OAUTH_2_BuildAuthorizationUri_requests_minimal_scopes_and_pkce_s256()
    {
        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id-123.apps.googleusercontent.com"
        };
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var pkce = PkceParameters.Create();
        var state = OAuthState.Generate();

        var uri = client.BuildAuthorizationUri(options.ClientId, "http://127.0.0.1:54321/", pkce, state);

        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("accounts.google.com");
        uri.AbsolutePath.Should().Be("/o/oauth2/v2/auth");

        var query = uri.Query;
        query.Should().Contain("client_id=test-client-id-123.apps.googleusercontent.com");
        query.Should().Contain("response_type=code");
        query.Should().Contain("access_type=offline");
        query.Should().Contain("prompt=consent");
        query.Should().Contain("code_challenge_method=S256");
        query.Should().Contain($"code_challenge={pkce.CodeChallenge}");
        query.Should().Contain($"state={state}");

        // SEC-OAUTH-2: Verify minimal scopes
        query.Should().Contain("calendar.events");
        query.Should().Contain("calendar.readonly");
        query.Should().Contain("openid");
    }

    [Fact]
    public async Task SEC_OAUTH_1_Authorize_rejects_mismatched_state_csrf()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions { ClientId = "test-client-id" };

        // Listener returns a callback with a forged/mismatched state
        _listener.WaitForCallbackAsync(Arg.Any<CancellationToken>())
            .Returns(new LoopbackCallbackResult
            {
                Code = "valid_auth_code",
                State = "forged_state_from_attacker"
            });

        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var result = await client.AuthorizeAsync(ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_state");
        result.ErrorDescription.Should().Contain("CSRF");

        // No token should have been stored
        await _secretStore.DidNotReceiveWithAnyArgs().SetSecretAsync(default!, default!, ct);
    }

    [Fact]
    public async Task FR_AUTH_1_Authorize_fails_when_user_denies_consent()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions { ClientId = "test-client-id" };

        _listener.WaitForCallbackAsync(Arg.Any<CancellationToken>())
            .Returns(new LoopbackCallbackResult
            {
                Error = "access_denied",
                ErrorDescription = "User denied access"
            });

        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var result = await client.AuthorizeAsync(ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("access_denied");
        result.ErrorDescription.Should().Be("User denied access");
    }

    [Fact]
    public async Task SEC_OAUTH_1_FR_AUTH_1_Authorize_succeeds_and_stores_refresh_token_in_secret_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        Uri? openedUri = null;
        await _browser.OpenBrowserAsync(Arg.Do<Uri>(u => openedUri = u), Arg.Any<CancellationToken>());

        _listener.WaitForCallbackAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Extract state from the URI passed to the browser to simulate genuine OAuth redirect
                var stateParam = ExtractQueryParameter(openedUri!.Query, "state");
                return Task.FromResult(new LoopbackCallbackResult
                {
                    Code = "google_auth_code_999",
                    State = stateParam
                });
            });

        // Fake id_token containing user profile
        var fakeIdToken = CreateFakeIdToken("sub-12345", "user@example.com", "Test User");

        var tokenResponseJson = JsonSerializer.Serialize(new
        {
            access_token = "mock_access_token_xyz",
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = "1//test_refresh_token_xyz",
            scope = "https://www.googleapis.com/auth/calendar.events openid",
            id_token = fakeIdToken
        });

        HttpRequestMessage? capturedTokenRequest = null;
        string? capturedRequestBody = null;
        var fakeHandler = new FakeHttpMessageHandler(async request =>
        {
            capturedTokenRequest = request;
            if (request.Content is not null)
            {
                capturedRequestBody = await request.Content.ReadAsStringAsync();
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenResponseJson, Encoding.UTF8, "application/json")
            };
            return response;
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var result = await client.AuthorizeAsync(ct);

        result.Success.Should().BeTrue();
        result.UserInfo.Should().NotBeNull();
        result.UserInfo!.Id.Should().Be("sub-12345");
        result.UserInfo.Email.Should().Be("user@example.com");
        result.UserInfo.DisplayName.Should().Be("Test User");

        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().Be("mock_access_token_xyz");
        result.Tokens.RefreshToken.Should().Be("1//test_refresh_token_xyz");

        // SEC-OAUTH-3, FR-AUTH-3: Refresh token must be saved under KnownSecretKeys.GoogleRefreshToken
        await _secretStore.Received(1).SetSecretAsync(
            KnownSecretKeys.GoogleRefreshToken("sub-12345"),
            "1//test_refresh_token_xyz",
            Arg.Any<CancellationToken>());

        // SEC-OAUTH-1: Verify token exchange request parameters (PKCE verifier, code, client_id, redirect_uri)
        capturedTokenRequest.Should().NotBeNull();
        capturedTokenRequest!.Method.Should().Be(HttpMethod.Post);
        capturedTokenRequest.RequestUri.Should().Be(new Uri(options.TokenEndpoint));

        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("client_id=test-client-id");
        capturedRequestBody.Should().Contain("client_secret=test-client-secret");
        capturedRequestBody.Should().Contain("code=google_auth_code_999");
        capturedRequestBody.Should().Contain("code_verifier=");
        capturedRequestBody.Should().Contain("grant_type=authorization_code");
    }

    [Fact]
    public async Task SEC_OAUTH_1_Authorize_handles_token_exchange_rejection()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions { ClientId = "test-client-id" };

        Uri? openedUri = null;
        await _browser.OpenBrowserAsync(Arg.Do<Uri>(u => openedUri = u), Arg.Any<CancellationToken>());

        _listener.WaitForCallbackAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var stateParam = ExtractQueryParameter(openedUri!.Query, "state");
                return Task.FromResult(new LoopbackCallbackResult
                {
                    Code = "invalid_code",
                    State = stateParam
                });
            });

        var errorResponseJson = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Bad Request (PKCE verification failed)"
        });

        var fakeHandler = new FakeHttpMessageHandler(async _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorResponseJson, Encoding.UTF8, "application/json")
            };
            return await Task.FromResult(response);
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var result = await client.AuthorizeAsync(ct);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
        result.ErrorDescription.Should().Contain("PKCE verification failed");
    }

    [Fact]
    public async Task FR_AUTH_3_RefreshAccessToken_succeeds_and_preserves_existing_refresh_token()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-001";
        const string existingRefreshToken = "mock_existing_refresh_token_aaa";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns(existingRefreshToken);

        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id-refresh"
        };

        string? capturedBody = null;
        var refreshResponseJson = JsonSerializer.Serialize(new
        {
            access_token = "mock_refreshed_access_token_111",
            token_type = "Bearer",
            expires_in = 3600,
            scope = "https://www.googleapis.com/auth/calendar.events"
        });

        var fakeHandler = new FakeHttpMessageHandler(async request =>
        {
            if (request.Content is not null)
            {
                capturedBody = await request.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(refreshResponseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var tokens = await client.RefreshAccessTokenAsync(accountId, ct);

        tokens.Should().NotBeNull();
        tokens.AccessToken.Should().Be("mock_refreshed_access_token_111");
        tokens.TokenType.Should().Be("Bearer");
        tokens.ExpiresInSeconds.Should().Be(3600);
        tokens.RefreshToken.Should().Be(existingRefreshToken);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("client_id=test-client-id-refresh");
        capturedBody.Should().Contain("grant_type=refresh_token");
        capturedBody.Should().Contain($"refresh_token={existingRefreshToken}");

        // Store did not need to update since Google did not rotate the refresh token
        await _secretStore.DidNotReceive().SetSecretAsync(
            KnownSecretKeys.GoogleRefreshToken(accountId),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_3_RefreshAccessToken_updates_stored_refresh_token_when_rotated()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-002";
        const string oldRefreshToken = "mock_old_refresh_token_bbb";
        const string newRotatedRefreshToken = "mock_new_rotated_refresh_token_ccc";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns(oldRefreshToken);

        var options = new GoogleOAuthOptions { ClientId = "test-client-id" };

        var refreshResponseJson = JsonSerializer.Serialize(new
        {
            access_token = "mock_refreshed_access_token_222",
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = newRotatedRefreshToken
        });

        var fakeHandler = new FakeHttpMessageHandler(async _ =>
        {
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(refreshResponseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var tokens = await client.RefreshAccessTokenAsync(accountId, ct);

        tokens.AccessToken.Should().Be("mock_refreshed_access_token_222");
        tokens.RefreshToken.Should().Be(newRotatedRefreshToken);

        // SEC-OAUTH-3, FR-AUTH-3: New rotated refresh token saved in DPAPI secret store
        await _secretStore.Received(1).SetSecretAsync(
            KnownSecretKeys.GoogleRefreshToken(accountId),
            newRotatedRefreshToken,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_3_FR_AUTH_6_RefreshAccessToken_throws_invalid_grant_and_clears_stored_token()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-003";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns("mock_expired_refresh_token_ddd");

        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            IsTestingConsentMode = true
        };

        var errorResponseJson = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been expired or revoked."
        });

        var fakeHandler = new FakeHttpMessageHandler(async _ =>
        {
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorResponseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var act = () => client.RefreshAccessTokenAsync(accountId, ct);

        var ex = await act.Should().ThrowAsync<GoogleInvalidGrantException>();
        ex.Which.AccountId.Should().Be(accountId);
        ex.Which.IsTestingConsentMode.Should().BeTrue();
        ex.Which.GuidanceMessage.Should().Contain("Testing");
        ex.Which.GuidanceMessage.Should().Contain("7 days");

        // FR-AUTH-3: Expired/revoked token deleted from DPAPI secret store
        await _secretStore.Received(1).DeleteSecretAsync(
            KnownSecretKeys.GoogleRefreshToken(accountId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_3_FR_AUTH_6_TryRefreshAccessToken_returns_failed_result_with_guidance_on_invalid_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-004";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns("mock_expired_refresh_token_eee");

        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            IsTestingConsentMode = true
        };

        var errorResponseJson = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been expired or revoked."
        });

        var fakeHandler = new FakeHttpMessageHandler(async _ =>
        {
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorResponseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var result = await client.TryRefreshAccessTokenAsync(accountId, ct);

        result.Success.Should().BeFalse();
        result.IsInvalidGrant.Should().BeTrue();
        result.RequiresReauthentication.Should().BeTrue();
        result.Error.Should().Be("invalid_grant");
        result.GuidanceMessage.Should().NotBeNull();
        result.GuidanceMessage.Should().Contain("Testing");
        result.GuidanceMessage.Should().Contain("7 days");
    }

    [Fact]
    public async Task FR_AUTH_3_RefreshAccessToken_throws_when_refresh_token_not_found_in_store()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-missing";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns((string?)null);

        var options = new GoogleOAuthOptions { ClientId = "test-client-id" };
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var act = () => client.RefreshAccessTokenAsync(accountId, ct);

        var ex = await act.Should().ThrowAsync<GoogleInvalidGrantException>();
        ex.Which.AccountId.Should().Be(accountId);

        var tryResult = await client.TryRefreshAccessTokenAsync(accountId, ct);
        tryResult.Success.Should().BeFalse();
        tryResult.RequiresReauthentication.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_6_RefreshAccessToken_throws_when_client_id_is_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new GoogleOAuthOptions { ClientId = "" };
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser);

        var act = () => client.RefreshAccessTokenAsync("account-123", ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var tryResult = await client.TryRefreshAccessTokenAsync("account-123", ct);
        tryResult.Success.Should().BeFalse();
        tryResult.Error.Should().Be("missing_client_id");
        tryResult.RequiresReauthentication.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_3_RefreshAccessToken_includes_client_secret_when_configured()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-005";
        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns("mock_token_fff");

        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "my-secret-credential"
        };

        string? capturedBody = null;
        var fakeHandler = new FakeHttpMessageHandler(async request =>
        {
            if (request.Content is not null)
            {
                capturedBody = await request.Content.ReadAsStringAsync();
            }

            var json = JsonSerializer.Serialize(new
            {
                access_token = "mock_access_token_333",
                token_type = "Bearer",
                expires_in = 3600
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        await client.RefreshAccessTokenAsync(accountId, ct);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("client_secret=my-secret-credential");
    }

    [Fact]
    public async Task FR_AUTH_6_Guidance_reflects_production_mode_when_testing_mode_is_false()
    {
        var ct = TestContext.Current.CancellationToken;
        const string accountId = "account-user-006";

        _secretStore.GetSecretAsync(KnownSecretKeys.GoogleRefreshToken(accountId), ct)
            .Returns("mock_expired_token_ggg");

        var options = new GoogleOAuthOptions
        {
            ClientId = "test-client-id",
            IsTestingConsentMode = false
        };

        var errorResponseJson = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been expired or revoked."
        });

        var fakeHandler = new FakeHttpMessageHandler(async _ =>
        {
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorResponseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(fakeHandler);
        var client = new GoogleOAuthClient(options, _secretStore, _listenerFactory, _browser, httpClient);

        var act = () => client.RefreshAccessTokenAsync(accountId, ct);

        var ex = await act.Should().ThrowAsync<GoogleInvalidGrantException>();
        ex.Which.IsTestingConsentMode.Should().BeFalse();
        ex.Which.GuidanceMessage.Should().Be(GoogleInvalidGrantException.ProductionModeDefaultGuidance);
        ex.Which.GuidanceMessage.Should().NotContain("7 days");
    }

    private static string ExtractQueryParameter(string query, string name)
    {
        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0] == name)
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return string.Empty;
    }

    private static string CreateFakeIdToken(string sub, string email, string name)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var payloadJson = JsonSerializer.Serialize(new
        {
            sub,
            email,
            name,
            aud = "test-client-id",
            iss = "https://accounts.google.com"
        });
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signature = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("fake_sig"));
        return $"{header}.{payload}.{signature}";
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
