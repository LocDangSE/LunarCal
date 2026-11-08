namespace LunarCal.Core.Chat;

/// <summary>
/// FR-BYOK-7: Vendor-neutral reason explaining why the model concluded its generation turn.
/// </summary>
public enum ChatFinishReason
{
    /// <summary>Natural stop token or completed utterance.</summary>
    Stop,

    /// <summary>The model requested execution of one or more tools.</summary>
    ToolCalls,

    /// <summary>The configured token limit was reached.</summary>
    Length,

    /// <summary>The content was blocked by provider safety/content moderation policies.</summary>
    Safety,

    /// <summary>Completed due to another condition or provider error.</summary>
    Other
}
