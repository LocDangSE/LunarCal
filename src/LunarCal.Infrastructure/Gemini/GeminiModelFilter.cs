using System.Text.Json;
using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Filters and extracts Gemini models suitable for function-calling calendar assistance (FR-BYOK-5, ADR-0016 §2).
/// Rules:
/// 1. Must support 'generateContent' in supportedGenerationMethods.
/// 2. Must exclude models containing 'tts', 'live', 'image', 'embedding', 'omni', 'latest'.
/// 3. Must exclude deprecated models starting with 'gemini-2.5'.
/// 4. Strips 'models/' prefix from model names.
/// </summary>
public static class GeminiModelFilter
{
    private static readonly string[] _excludedSubstrings =
    [
        "tts",
        "live",
        "image",
        "embedding",
        "omni",
        "latest",
    ];

    private static readonly HashSet<string> _recommendedModelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini-3.8-flash",
        "gemini-3.6-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite",
    };

    /// <summary>
    /// Checks whether a model with the given name and generation methods is eligible for LunarCal.
    /// </summary>
    public static bool IsSupportedModel(string? rawName, IEnumerable<string>? supportedMethods)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var normalizedId = NormalizeModelId(rawName);

        // Rule 1: Must support generateContent
        var hasGenerateContent = supportedMethods != null &&
            supportedMethods.Any(m => string.Equals(m, "generateContent", StringComparison.OrdinalIgnoreCase));

        if (!hasGenerateContent)
        {
            return false;
        }

        // Rule 2: Exclude names containing tts, live, image, embedding, omni, latest
        foreach (var excluded in _excludedSubstrings)
        {
            if (normalizedId.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Rule 3: Exclude deprecated gemini-2.5-* models (ADR-0016 §1)
        if (normalizedId.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a model identifier by stripping the 'models/' prefix if present.
    /// </summary>
    public static string NormalizeModelId(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var trimmed = rawName.Trim();
        return trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? trimmed["models/".Length..]
            : trimmed;
    }

    /// <summary>
    /// Filters and parses models from JSON returned by GET /v1beta/models.
    /// </summary>
    public static IReadOnlyList<AiModelInfo> FilterModels(string json, string? defaultModelId = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return GetFallbackModels(defaultModelId);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return FilterModels(doc, defaultModelId);
        }
        catch (JsonException)
        {
            return GetFallbackModels(defaultModelId);
        }
    }

    /// <summary>
    /// Filters and parses models from a JsonDocument returned by GET /v1beta/models.
    /// </summary>
    public static IReadOnlyList<AiModelInfo> FilterModels(JsonDocument doc, string? defaultModelId = null)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var effectiveDefault = !string.IsNullOrWhiteSpace(defaultModelId) ? defaultModelId : "gemini-3.8-flash";
        var result = new List<AiModelInfo>();

        if (doc.RootElement.TryGetProperty("models", out var modelsArray) &&
            modelsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var modelElement in modelsArray.EnumerateArray())
            {
                if (!modelElement.TryGetProperty("name", out var nameProp))
                {
                    continue;
                }

                var rawName = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                var methods = new List<string>();
                if (modelElement.TryGetProperty("supportedGenerationMethods", out var methodsElement) &&
                    methodsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in methodsElement.EnumerateArray())
                    {
                        var methodStr = m.GetString();
                        if (!string.IsNullOrWhiteSpace(methodStr))
                        {
                            methods.Add(methodStr);
                        }
                    }
                }

                if (!IsSupportedModel(rawName, methods))
                {
                    continue;
                }

                var id = NormalizeModelId(rawName);

                var displayName = modelElement.TryGetProperty("displayName", out var displayProp) &&
                                  !string.IsNullOrWhiteSpace(displayProp.GetString())
                    ? displayProp.GetString()!
                    : FormatFriendlyName(id);

                var description = modelElement.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : null;

                int? inputTokenLimit = modelElement.TryGetProperty("inputTokenLimit", out var inLimit) && inLimit.TryGetInt32(out var inVal)
                    ? inVal
                    : null;

                int? outputTokenLimit = modelElement.TryGetProperty("outputTokenLimit", out var outLimit) && outLimit.TryGetInt32(out var outVal)
                    ? outVal
                    : null;

                var isDefault = string.Equals(id, effectiveDefault, StringComparison.OrdinalIgnoreCase);
                var isRecommended = _recommendedModelIds.Contains(id);

                result.Add(new AiModelInfo(
                    id,
                    displayName,
                    description,
                    inputTokenLimit,
                    outputTokenLimit,
                    isDefault,
                    isRecommended));
            }
        }

        if (result.Count == 0)
        {
            return GetFallbackModels(effectiveDefault);
        }

        // Sort: Recommended and default first, then alphabetical
        return result
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.IsRecommended)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the recommended fallback models when offline or when API listing is unavailable (ADR-0016 §1).
    /// </summary>
    public static IReadOnlyList<AiModelInfo> GetFallbackModels(string? defaultModelId = null)
    {
        var effectiveDefault = !string.IsNullOrWhiteSpace(defaultModelId) ? defaultModelId : "gemini-3.8-flash";

        var models = new List<AiModelInfo>
        {
            new(
                "gemini-3.8-flash",
                "Gemini 3.8 Flash",
                "Fast, versatile, and high intelligence model with function calling support.",
                1048576,
                65536,
                IsDefault: string.Equals("gemini-3.8-flash", effectiveDefault, StringComparison.OrdinalIgnoreCase),
                IsRecommended: true),
            new(
                "gemini-3.6-flash",
                "Gemini 3.6 Flash",
                "Stable multimodal model with lower resource latency.",
                1048576,
                65536,
                IsDefault: string.Equals("gemini-3.6-flash", effectiveDefault, StringComparison.OrdinalIgnoreCase),
                IsRecommended: true),
            new(
                "gemini-3.5-flash-lite",
                "Gemini 3.5 Flash Lite",
                "Lightweight model optimized for rapid response times and cost efficiency.",
                1048576,
                65536,
                IsDefault: string.Equals("gemini-3.5-flash-lite", effectiveDefault, StringComparison.OrdinalIgnoreCase),
                IsRecommended: true),
            new(
                "gemini-3.1-flash-lite",
                "Gemini 3.1 Flash Lite",
                "Cost-effective lightweight model for basic command interpretation.",
                1048576,
                65536,
                IsDefault: string.Equals("gemini-3.1-flash-lite", effectiveDefault, StringComparison.OrdinalIgnoreCase),
                IsRecommended: true),
        };

        return models
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.IsRecommended)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatFriendlyName(string modelId)
    {
        return modelId switch
        {
            "gemini-3.8-flash" => "Gemini 3.8 Flash",
            "gemini-3.6-flash" => "Gemini 3.6 Flash",
            "gemini-3.5-flash-lite" => "Gemini 3.5 Flash Lite",
            "gemini-3.1-flash-lite" => "Gemini 3.1 Flash Lite",
            _ => modelId
        };
    }
}
