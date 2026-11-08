using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Settings;
using Microsoft.Extensions.Options;
using Serilog;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Implements <see cref="IAiModelService"/> for querying available Gemini models, persisting user selection,
/// and validating function calling support via probe requests (FR-BYOK-5, ADR-0016 §2).
/// </summary>
public sealed class GeminiModelService : IAiModelService
{
    private const string ModelsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly GeminiOptions _options;
    private readonly ILogger _logger;

    private IReadOnlyList<AiModelInfo>? _cachedModels;

    public GeminiModelService(
        HttpClient httpClient,
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        IOptions<GeminiOptions>? options = null,
        ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _options = options?.Value ?? new GeminiOptions();
        _logger = logger ?? Log.ForContext<GeminiModelService>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedModels != null && _cachedModels.Count > 0)
        {
            return _cachedModels;
        }

        var apiKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _cachedModels = GeminiModelFilter.GetFallbackModels(_options.DefaultModelId);
            return _cachedModels;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
            request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey.Trim());

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var filtered = GeminiModelFilter.FilterModels(json, _options.DefaultModelId);
                if (filtered.Count > 0)
                {
                    _cachedModels = filtered;
                    return _cachedModels;
                }
            }
            else
            {
                _logger.Warning("Failed to list Gemini models from API, HTTP {StatusCode}", response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error fetching Gemini models from API, using fallback models");
        }

        _cachedModels = GeminiModelFilter.GetFallbackModels(_options.DefaultModelId);
        return _cachedModels;
    }

    /// <inheritdoc />
    public async Task<string> GetSelectedModelIdAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _settingsStore.GetStringAsync(SettingKeys.GeminiModelId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        return !string.IsNullOrWhiteSpace(_options.DefaultModelId)
            ? _options.DefaultModelId
            : "gemini-3.8-flash";
    }

    /// <inheritdoc />
    public async Task<bool> SetSelectedModelAsync(string modelId, bool validateFunctionCalling = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model ID cannot be null or empty.", nameof(modelId));
        }

        var normalizedId = GeminiModelFilter.NormalizeModelId(modelId);

        if (validateFunctionCalling)
        {
            var apiKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var supported = await ValidateFunctionCallingSupportAsync(normalizedId, cancellationToken).ConfigureAwait(false);
                if (!supported)
                {
                    _logger.Warning("Model {ModelId} failed function calling validation", normalizedId);
                    return false;
                }
            }
        }

        await _settingsStore.SetStringAsync(SettingKeys.GeminiModelId, normalizedId, cancellationToken).ConfigureAwait(false);
        _logger.Information("Saved selected Gemini model: {ModelId}", normalizedId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateFunctionCallingSupportAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var apiKey = await _secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.Warning("Cannot validate function calling: no Gemini API key available");
            return false;
        }

        var normalizedId = GeminiModelFilter.NormalizeModelId(modelId);
        var probeEndpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{normalizedId}:generateContent";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = "ping" } }
                }
            },
            tools = new[]
            {
                new
                {
                    functionDeclarations = new[]
                    {
                        new
                        {
                            name = "test_ping",
                            description = "Test function calling capability",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new { }
                            }
                        }
                    }
                }
            },
            toolConfig = new
            {
                functionCallingConfig = new
                {
                    mode = "AUTO"
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, probeEndpoint);
            request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey.Trim());
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // SEC-LOG-2, AGENTS §7: never log the raw Gemini response at the default level; the status code is enough.
            _logger.Warning("Function calling probe failed for {ModelId}: HTTP {Status}", normalizedId, response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Function calling probe encountered network or execution error for {ModelId}", normalizedId);
            return false;
        }
    }
}
