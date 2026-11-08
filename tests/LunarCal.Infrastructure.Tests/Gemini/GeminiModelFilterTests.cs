using AwesomeAssertions;
using LunarCal.Infrastructure.Gemini;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiModelFilter"/> (FR-BYOK-5, ADR-0016 §2).
/// Verifies filtering of models from models.list, exclusion rules (tts, live, image, embedding, omni, latest, 2.5),
/// and fallback model generation.
/// </summary>
public sealed class GeminiModelFilterTests
{
    [Theory]
    [InlineData("models/gemini-3.8-flash", true)]
    [InlineData("models/gemini-3.6-flash", true)]
    [InlineData("models/gemini-3.5-flash-lite", true)]
    [InlineData("models/gemini-3.1-flash-lite", true)]
    [InlineData("custom-gemini-model", true)]
    public void FR_BYOK_5_IsSupportedModel_accepts_valid_names_with_generateContent(string modelName, bool expected)
    {
        var supported = GeminiModelFilter.IsSupportedModel(modelName, ["generateContent"]);
        supported.Should().Be(expected);
    }

    [Fact]
    public void FR_BYOK_5_IsSupportedModel_rejects_models_without_generateContent()
    {
        var supported = GeminiModelFilter.IsSupportedModel("models/text-embedding-004", ["embedContent"]);
        supported.Should().BeFalse();
    }

    [Theory]
    [InlineData("models/gemini-3.0-flash-tts")]
    [InlineData("models/gemini-live-2.0")]
    [InlineData("models/imagen-3.0-generate-002")]
    [InlineData("models/embedding-gecko-001")]
    [InlineData("models/gemini-omni-experimental")]
    [InlineData("models/gemini-flash-latest")]
    [InlineData("models/gemini-2.5-flash")]
    [InlineData("models/gemini-2.5-pro")]
    public void FR_BYOK_5_IsSupportedModel_rejects_excluded_names_and_deprecated_2_5(string modelName)
    {
        var supported = GeminiModelFilter.IsSupportedModel(modelName, ["generateContent"]);
        supported.Should().BeFalse();
    }

    [Fact]
    public void FR_BYOK_5_FilterModels_parses_json_and_filters_correctly()
    {
        const string json = """
        {
          "models": [
            {
              "name": "models/gemini-3.8-flash",
              "displayName": "Gemini 3.8 Flash",
              "description": "Fast and versatile",
              "inputTokenLimit": 1048576,
              "outputTokenLimit": 65536,
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-flash-latest",
              "displayName": "Latest Flash",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/gemini-2.5-flash",
              "displayName": "Deprecated 2.5",
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/text-embedding-004",
              "displayName": "Embedding 004",
              "supportedGenerationMethods": ["embedContent"]
            },
            {
              "name": "models/gemini-3.6-flash",
              "displayName": "Gemini 3.6 Flash",
              "supportedGenerationMethods": ["generateContent"]
            }
          ]
        }
        """;

        var models = GeminiModelFilter.FilterModels(json, defaultModelId: "gemini-3.8-flash");

        models.Should().HaveCount(2);
        models.Select(m => m.Id).Should().Equal("gemini-3.8-flash", "gemini-3.6-flash");

        var flash38 = models.First(m => m.Id == "gemini-3.8-flash");
        flash38.DisplayName.Should().Be("Gemini 3.8 Flash");
        flash38.IsDefault.Should().BeTrue();
        flash38.IsRecommended.Should().BeTrue();
        flash38.InputTokenLimit.Should().Be(1048576);
        flash38.OutputTokenLimit.Should().Be(65536);
    }

    [Fact]
    public void FR_BYOK_5_GetFallbackModels_returns_4_recommended_models_with_default()
    {
        var fallbacks = GeminiModelFilter.GetFallbackModels("gemini-3.6-flash");

        fallbacks.Should().HaveCount(4);
        fallbacks.Select(m => m.Id).Should().Contain(["gemini-3.8-flash", "gemini-3.6-flash", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite"]);

        var defaultModel = fallbacks.First(m => m.IsDefault);
        defaultModel.Id.Should().Be("gemini-3.6-flash");
    }

    [Theory]
    [InlineData("models/gemini-3.8-flash", "gemini-3.8-flash")]
    [InlineData("gemini-3.8-flash", "gemini-3.8-flash")]
    [InlineData("  models/custom  ", "custom")]
    public void FR_BYOK_5_NormalizeModelId_strips_models_prefix(string input, string expected)
    {
        GeminiModelFilter.NormalizeModelId(input).Should().Be(expected);
    }
}
