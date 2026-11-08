using System.Net;
using System.Text.Json;
using LunarCal.Core.Abstractions;
using Serilog;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Validates a Gemini API key by querying GET /v1beta/models (FR-BYOK-2, ADR-0016 §2, §4).
/// </summary>
public sealed class GeminiKeyValidator : IAiKeyValidator
{
    private const string ModelsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public GeminiKeyValidator(HttpClient httpClient, ILogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? Log.ForContext<GeminiKeyValidator>();
    }

    public async Task<KeyValidationResult> ValidateKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return KeyValidationResult.Failed(KeyValidationStatus.EmptyKey, "API key cannot be empty.");
        }

        var trimmedKey = apiKey.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, trimmedKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var models = ExtractModelIds(content);
                return KeyValidationResult.Success(models);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var classification = GeminiErrorClassifier.Classify(response.StatusCode, errorBody);

            var status = classification.Kind switch
            {
                GeminiErrorKind.LeakedApiKey => KeyValidationStatus.LeakedKey,
                GeminiErrorKind.FreeTierVerificationRequired => KeyValidationStatus.FreeTierVerificationRequired,
                GeminiErrorKind.InvalidApiKey => KeyValidationStatus.InvalidKey,
                GeminiErrorKind.FailedPrecondition => KeyValidationStatus.PreconditionFailed,
                GeminiErrorKind.RateLimitExceeded => KeyValidationStatus.QuotaExceeded,
                GeminiErrorKind.DailyQuotaExceeded => KeyValidationStatus.QuotaExceeded,
                _ => response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => KeyValidationStatus.InvalidKey,
                    HttpStatusCode.BadRequest => KeyValidationStatus.InvalidKey,
                    _ => KeyValidationStatus.UnexpectedError
                }
            };

            return KeyValidationResult.Failed(status, classification.Description);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            _logger.Warning("Network failure while validating Gemini API key: {Message}", ex.Message);
            return KeyValidationResult.Failed(KeyValidationStatus.NetworkError, "Network failure or timeout contacting Gemini endpoint.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error during Gemini key validation");
            return KeyValidationResult.Failed(KeyValidationStatus.UnexpectedError, ex.Message);
        }
    }

    private static List<string> ExtractModelIds(string json)
    {
        var modelIds = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var modelsElement) &&
                modelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsElement.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                                ? name["models/".Length..]
                                : name;
                            modelIds.Add(id);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors on model list extraction
        }

        return modelIds;
    }
}
