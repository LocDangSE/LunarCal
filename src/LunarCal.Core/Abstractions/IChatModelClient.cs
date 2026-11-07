using LunarCal.Core.Chat;

namespace LunarCal.Core.Abstractions;

/// <summary>
/// FR-BYOK-7: Vendor-neutral contract for chat model providers.
/// Isolates the Core domain from vendor-specific REST/SDK implementations (Gemini, etc., spec §7).
/// </summary>
public interface IChatModelClient
{
    /// <summary>
    /// Sends a chat completion request to the model provider.
    /// </summary>
    /// <param name="request">Vendor-neutral chat request containing prompt, messages and tools.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A vendor-neutral chat response.</returns>
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
