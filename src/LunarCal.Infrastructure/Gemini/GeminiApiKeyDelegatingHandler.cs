using System.Security;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Dedicated delegating handler for Gemini API requests (ADR-0016 §2, SEC-KEY-2, SEC-KEY-3).
/// Injects the API key into the 'x-goog-api-key' header just-in-time, strictly enforces the target host
/// (generativelanguage.googleapis.com), prevents key leaks in query strings, and registers the key value in
/// <see cref="SecretValueRegistry"/> for universal redaction.
/// </summary>
public sealed class GeminiApiKeyDelegatingHandler : DelegatingHandler
{
    private const string AllowedGeminiHost = "generativelanguage.googleapis.com";
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private readonly ISecretStore _secretStore;
    private readonly ILogger<GeminiApiKeyDelegatingHandler>? _logger;

    public GeminiApiKeyDelegatingHandler(
        ISecretStore secretStore,
        ILogger<GeminiApiKeyDelegatingHandler>? logger = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Host restriction (ADR-0016 §2, SEC-KEY-2)
        var host = request.RequestUri?.Host;
        if (!string.Equals(host, AllowedGeminiHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException(
                $"Gemini API key handler only permits requests to {AllowedGeminiHost}, but target host was '{host}' (SEC-KEY-2, ADR-0016).");
        }

        // 2. Query string inspection - key must never appear in query string (SEC-KEY-2)
        var query = request.RequestUri?.Query;
        if (!string.IsNullOrEmpty(query))
        {
            if (query.Contains("key=", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("api_key=", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("x-goog-api-key=", StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Gemini API key must not be passed in query string (SEC-KEY-2).");
            }
        }

        // 3. Resolve key just-in-time from ISecretStore (SEC-KEY-3)
        var apiKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiApiException(
                GeminiErrorKind.MissingApiKey,
                "Gemini API key is not configured. Please enter your API key in Settings > AI.");
        }

        // 4. Register key value for redaction (ADR-0016 §2)
        SecretValueRegistry.Register(apiKey);

        // 5. Attach header
        request.Headers.Remove(ApiKeyHeaderName);
        request.Headers.Add(ApiKeyHeaderName, apiKey);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
