namespace LunarCal.Core.Abstractions;

/// <summary>
/// Validates an AI provider API key against the provider's validation endpoint (FR-BYOK-2, ADR-0016 §2).
/// </summary>
public interface IAiKeyValidator
{
    /// <summary>
    /// Validates the given API key.
    /// </summary>
    /// <param name="apiKey">The raw API key to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result detailing success or categorized failure.</returns>
    Task<KeyValidationResult> ValidateKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}
