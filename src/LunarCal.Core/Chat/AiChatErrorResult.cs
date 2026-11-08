namespace LunarCal.Core.Chat;

/// <summary>
/// Evaluated result of mapping an AI chat error (FR-CHAT-7).
/// Provides UI navigation hints, retry policy decisions, and localized message keys.
/// </summary>
public sealed record AiChatErrorResult(
    AiChatErrorKind Kind,
    AiChatErrorAction Action,
    string MessageKey,
    string? TechnicalDetails = null,
    TimeSpan? RetryAfter = null,
    bool CanRetry = false,
    bool RequiresSettingsNavigation = false,
    bool IsQueuedLocally = false);
