namespace LunarCal.Core.Chat;

/// <summary>
/// Prescribed application action in response to an AI chat error (FR-CHAT-7, ADR-0016 §4).
/// </summary>
public enum AiChatErrorAction
{
    /// <summary>No special action required; display generic notification.</summary>
    None = 0,

    /// <summary>Open Settings &gt; AI page to allow the user to check, configure, or renew the API key / model.</summary>
    OpenAiSettings,

    /// <summary>Wait for cooldown period and retry with exponential backoff and jitter.</summary>
    WaitForRetry,

    /// <summary>Preserve command in local SQLite queue and automatically process when network becomes available.</summary>
    QueueOffline,

    /// <summary>Notify user clearly that generation was blocked by Google safety filters; do not retry.</summary>
    NotifySafetyBlock,

    /// <summary>Notify user that daily quota was exhausted (resets midnight Pacific); do not retry immediately.</summary>
    NotifyDailyQuotaExceeded
}
