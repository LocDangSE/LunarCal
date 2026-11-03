using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using Serilog;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Google OAuth 2.0 client implementing the desktop authorization code flow with PKCE S256 and loopback redirect
/// (FR-AUTH-1, FR-AUTH-2, FR-AUTH-6, SEC-OAUTH-1, SEC-OAUTH-2, SEC-OAUTH-3, ADR-0017).
/// </summary>
public sealed class GoogleOAuthClient : IGoogleOAuthClient
{
    private readonly GoogleOAuthOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly ILoopbackHttpListenerFactory _listenerFactory;
    private readonly ISystemBrowser _browser;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public GoogleOAuthClient(
        GoogleOAuthOptions options,
        ISecretStore secretStore,
        ILoopbackHttpListenerFactory? listenerFactory = null,
        ISystemBrowser? browser = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretStore);

        _options = options;
        _secretStore = secretStore;
        _listenerFactory = listenerFactory ?? new LoopbackHttpListenerFactory();
        _browser = browser ?? new SystemBrowser();
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? Log.ForContext<GoogleOAuthClient>();
    }

    /// <inheritdoc />
    public async Task<GoogleAuthResult> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        var clientId = _options.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.Error("Google OAuth Client ID is not configured (FR-AUTH-6).");
            return GoogleAuthResult.Failed("missing_client_id", "Google OAuth Client ID is not configured.");
        }

        var timeout = TimeSpan.FromSeconds(_options.LoopbackTimeoutSeconds > 0 ? _options.LoopbackTimeoutSeconds : 300);

        // SEC-OAUTH-1: loopback only binds 127.0.0.1 on a dynamic port
        await using var listener = _listenerFactory.CreateListener(timeout);
        listener.Start();

        var pkce = PkceParameters.Create();
        var state = OAuthState.Generate();

        var authUri = BuildAuthorizationUri(clientId, listener.RedirectUri, pkce, state);

        _logger.Information("Starting Google OAuth desktop authorization on loopback redirect {RedirectUri}.", listener.RedirectUri);

        // FR-AUTH-1: Open system default browser (no embedded WebView)
        await _browser.OpenBrowserAsync(authUri, cancellationToken).ConfigureAwait(false);

        // SEC-OAUTH-1: Wait for callback with timeout (listener stops immediately after callback or timeout)
        LoopbackCallbackResult callback;
        try
        {
            callback = await listener.WaitForCallbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.Warning(ex, "OAuth loopback listener timed out waiting for user response.");
            return GoogleAuthResult.Failed("timeout", $"Authentication timed out after {timeout.TotalSeconds} seconds.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GoogleAuthResult.Failed("canceled", "Authentication was canceled by user.");
        }

        // Check if user denied consent or error occurred
        if (!string.IsNullOrEmpty(callback.Error))
        {
            _logger.Warning("OAuth callback returned error {Error}: {Description}", callback.Error, callback.ErrorDescription);
            return GoogleAuthResult.Failed(callback.Error, callback.ErrorDescription);
        }

        // SEC-OAUTH-1: Validate state parameter against CSRF attacks
        if (!OAuthState.Validate(state, callback.State))
        {
            _logger.Error("OAuth state validation failed. Received state does not match expected state (SEC-OAUTH-1).");
            return GoogleAuthResult.Failed("invalid_state", "Invalid state parameter received from callback (possible CSRF attack).");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return GoogleAuthResult.Failed("missing_code", "No authorization code received from callback.");
        }

        // Exchange authorization code for tokens (PKCE verifier sent)
        var tokenResult = await ExchangeCodeForTokensAsync(clientId, listener.RedirectUri, callback.Code, pkce.CodeVerifier, cancellationToken).ConfigureAwait(false);
        if (!tokenResult.Success || tokenResult.Tokens is null)
        {
            return GoogleAuthResult.Failed(tokenResult.Error ?? "token_exchange_failed", tokenResult.ErrorDescription);
        }

        var tokens = tokenResult.Tokens;

        // Parse user information from ID Token or fallback to userinfo endpoint
        var userInfo = GoogleUserInfoParser.ParseIdToken(tokens.IdToken);
        if (userInfo is null)
        {
            userInfo = await FetchUserInfoAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        }

        if (userInfo is null)
        {
            _logger.Error("Failed to parse or fetch Google account user profile.");
            return GoogleAuthResult.Failed("user_profile_unavailable", "Could not obtain user profile from Google.");
        }

        // SEC-OAUTH-3, FR-AUTH-3: Refresh token stored only in DPAPI secret store
        if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            var secretKey = KnownSecretKeys.GoogleRefreshToken(userInfo.Id);
            await _secretStore.SetSecretAsync(secretKey, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            _logger.Information("Google refresh token securely saved in DPAPI secret store for account {AccountId}.", userInfo.Id);
        }

        return GoogleAuthResult.Succeeded(userInfo, tokens);
    }

    /// <summary>
    /// Builds the authorization URI with PKCE parameters, state, and minimal requested scopes (FR-AUTH-1, FR-AUTH-2, SEC-OAUTH-1).
    /// </summary>
    public Uri BuildAuthorizationUri(string clientId, string redirectUri, PkceParameters pkce, string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentNullException.ThrowIfNull(pkce);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var scopes = string.Join(" ", _options.Scopes);

        var url = $"{_options.AuthorizationEndpoint}?" +
                  $"client_id={UrlEncoder.Default.Encode(clientId)}&" +
                  $"redirect_uri={UrlEncoder.Default.Encode(redirectUri)}&" +
                  $"response_type=code&" +
                  $"scope={UrlEncoder.Default.Encode(scopes)}&" +
                  $"code_challenge={UrlEncoder.Default.Encode(pkce.CodeChallenge)}&" +
                  $"code_challenge_method={UrlEncoder.Default.Encode(PkceParameters.CodeChallengeMethod)}&" +
                  $"state={UrlEncoder.Default.Encode(state)}&" +
                  $"access_type=offline&" +
                  $"prompt=consent";

        return new Uri(url);
    }

    private async Task<TokenExchangeResult> ExchangeCodeForTokensAsync(
        string clientId,
        string redirectUri,
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = authorizationCode,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            parameters["client_secret"] = _options.ClientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("Token exchange failed with HTTP status {StatusCode}.", (int)response.StatusCode);
                return ParseTokenError(content, (int)response.StatusCode);
            }

            return ParseTokenSuccess(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "HTTP exception during token exchange with Google.");
            return TokenExchangeResult.Failed("network_error", ex.Message);
        }
    }

    private static TokenExchangeResult ParseTokenSuccess(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()!;
        var tokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString()! : "Bearer";
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
        var idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;

        var tokens = new GoogleOAuthTokens
        {
            AccessToken = accessToken,
            TokenType = tokenType,
            ExpiresInSeconds = expiresIn,
            RefreshToken = refreshToken,
            Scope = scope,
            IdToken = idToken
        };

        return TokenExchangeResult.Succeeded(tokens);
    }

    private static TokenExchangeResult ParseTokenError(string json, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : $"http_{statusCode}";
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return TokenExchangeResult.Failed(error ?? $"http_{statusCode}", desc);
        }
        catch
        {
            return TokenExchangeResult.Failed($"http_{statusCode}", $"Server returned HTTP {statusCode}");
        }
    }

    private async Task<GoogleUserInfo?> FetchUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GoogleUserInfoParser.ParseUserInfoJson(json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to retrieve user profile from userinfo endpoint.");
            return null;
        }
    }

    private sealed record TokenExchangeResult
    {
        public bool Success { get; init; }
        public GoogleOAuthTokens? Tokens { get; init; }
        public string? Error { get; init; }
        public string? ErrorDescription { get; init; }

        public static TokenExchangeResult Succeeded(GoogleOAuthTokens tokens) =>
            new() { Success = true, Tokens = tokens };

        public static TokenExchangeResult Failed(string error, string? description = null) =>
            new() { Success = false, Error = error, ErrorDescription = description };
    }
}
