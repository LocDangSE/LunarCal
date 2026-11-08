namespace LunarCal.Core.Chat;

/// <summary>
/// Maps AI execution exceptions, status codes, and error messages to structured
/// <see cref="AiChatErrorResult"/> decisions (FR-CHAT-7, ADR-0016 §4).
/// </summary>
public interface IAiChatErrorMapper
{
    /// <summary>
    /// Maps an exception encountered during chat execution to a structured decision result.
    /// </summary>
    AiChatErrorResult Map(Exception? exception);

    /// <summary>
    /// Maps an error message string or status string to a structured decision result.
    /// </summary>
    AiChatErrorResult MapMessage(string? errorMessage);
}
