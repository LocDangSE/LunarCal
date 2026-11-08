namespace LunarCal.Core.Abstractions;

/// <summary>
/// Manages the lifecycle, masking and secure storage of the user's BYOK AI key (FR-BYOK-1..4, SEC-KEY-1..11, ADR-0016).
/// </summary>
public interface IAiKeyService
{
    /// <summary>
    /// Checks if a key is currently configured in secret storage.
    /// </summary>
    Task<bool> HasKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current key formatted as a masked string (e.g. "AIza••••xxxx" or "••••xxxx"),
    /// or null if no key is configured.
    /// </summary>
    Task<string?> GetMaskedKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and saves the API key into secret storage (SEC-KEY-6).
    /// If <paramref name="saveWithoutValidation"/> is true (offline fallback), saves without network validation.
    /// </summary>
    Task<KeyValidationResult> ValidateAndSaveKeyAsync(
        string apiKey,
        bool saveWithoutValidation = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the currently configured key from secret storage and unregisters it from runtime redaction.
    /// </summary>
    Task DeleteKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a suspected key leak (FR-BYOK-4, SEC-KEY-7): deletes the key immediately,
    /// unregisters it, and signals to pause pending AI commands.
    /// </summary>
    Task SuspectLeakedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Formats a raw key into its masked representation.
    /// </summary>
    string MaskKey(string apiKey);

    /// <summary>
    /// Indicates whether the AI command queue is currently paused due to suspected leak or revocation.
    /// </summary>
    bool IsQueuePaused { get; }

    /// <summary>
    /// Raised when the queue pause state changes.
    /// </summary>
    event EventHandler<bool>? QueuePausedChanged;
}
