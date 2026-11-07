namespace LunarCal.Core.Abstractions;

/// <summary>
/// Lets the user replace the build's OAuth client with their own Google Cloud client (FR-AUTH-6, Figma S20).
/// The client ID is not secret and lives in AppSetting; the client secret is kept only in <see cref="ISecretStore"/>
/// (SEC-KEY-1). Applying an override changes the client used by the next sign-in and token refresh.
/// </summary>
public interface IOAuthClientOverrideService
{
    /// <summary>The custom client ID in use, or <see langword="null"/> when the build's client is used.</summary>
    string? CustomClientId { get; }

    /// <summary>Whether a custom client secret is stored.</summary>
    bool HasCustomClientSecret { get; }

    /// <summary>Loads the stored override (if any) and applies it. Call once at startup.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores and applies a custom client. An empty secret keeps the stored one.</summary>
    Task SaveAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default);

    /// <summary>Removes the override and returns to the build's client.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
