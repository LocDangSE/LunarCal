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
    /// Maximum output tokens allowed per turn (default: 4096). Thinking tokens count against this limit, so it is
    /// larger than the short tool call itself needs (ADR-0016, P4-R6).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>
    /// Thinking level sent as <c>generationConfig.thinkingConfig.thinkingLevel</c> (default "low": calendar commands are
    /// short and structured, and the model default "medium" took about a minute per call, P4-R6). Empty keeps the
    /// model default. The client drops it for good if the API rejects it.
    /// </summary>
    public string? ThinkingLevel { get; set; } = "low";
}
