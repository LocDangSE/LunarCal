using System.Text.Json;

namespace LunarCal.Core.Chat;

/// <summary>
/// FR-QUEUE-2, FR-CONFIRM-1, FR-CONFIRM-8: What the model proposed for a queued command, persisted on the
/// <c>AiCommand</c> row (<c>proposal_json</c>) so that a command waiting in "Chờ xác nhận" survives restarts and the
/// confirmation dialog shows the real proposal instead of re-deriving one (P4-REVIEW, ADR-0031).
/// </summary>
/// <param name="ToolCall">The validated tool call awaiting user confirmation, or null while the model asks a question.</param>
/// <param name="ClarificationQuestion">The question the model asked when it needs more information (S16).</param>
/// <param name="ClarificationAnswer">The user's answer to <paramref name="ClarificationQuestion"/>, once given.</param>
public sealed record AiCommandProposal(
    ToolCall? ToolCall,
    string? ClarificationQuestion = null,
    string? ClarificationAnswer = null)
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Serializes the proposal for storage.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, _json);

    /// <summary>Parses a stored proposal; returns null for null, blank or unreadable JSON.</summary>
    public static AiCommandProposal? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AiCommandProposal>(json, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
