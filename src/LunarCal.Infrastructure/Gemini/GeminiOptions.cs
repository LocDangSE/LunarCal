namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Options for configuring the Gemini chat model integration (spec §7, ADR-0016).
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "LunarCal:Gemini";

    /// <summary>
    /// Base URL for the Gemini REST API. Default: "https://generativelanguage.googleapis.com/".
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";

    /// <summary>
    /// Default Gemini model ID (e.g. "gemini-3.8-flash", ADR-0016).
    /// </summary>
    public string DefaultModelId { get; set; } = "gemini-3.8-flash";

    /// <summary>
    /// HTTP request timeout in seconds (default: 60).
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum output tokens allowed per turn (default: 2048, ADR-0016).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;
}
