namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Data payload returned from the OAuth redirect callback (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public sealed record LoopbackCallbackResult
{
    /// <summary>Authorization code returned from Google.</summary>
    public string? Code { get; init; }

    /// <summary>State token echoed back from Google.</summary>
    public string? State { get; init; }

    /// <summary>Error code if authorization was denied or failed.</summary>
    public string? Error { get; init; }

    /// <summary>Error description if provided.</summary>
    public string? ErrorDescription { get; init; }
}

/// <summary>
/// Loopback HTTP listener contract bound to 127.0.0.1 on a dynamic port (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public interface ILoopbackHttpListener : IAsyncDisposable, IDisposable
{
    /// <summary>Local port assigned to the loopback listener.</summary>
    int Port { get; }

    /// <summary>Full redirect URI (e.g. "http://127.0.0.1:{Port}/").</summary>
    string RedirectUri { get; }

    /// <summary>Whether the listener is actively waiting for incoming connections.</summary>
    bool IsListening { get; }

    /// <summary>Starts listening on 127.0.0.1:{Port}.</summary>
    void Start();

    /// <summary>
    /// Waits for the incoming browser redirect, sends an HTML confirmation page,
    /// and immediately stops the listener (SEC-OAUTH-1).
    /// </summary>
    Task<LoopbackCallbackResult> WaitForCallbackAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the listener and closes the port.</summary>
    void StopListening();
}

/// <summary>
/// Factory for creating loopback HTTP listeners.
/// </summary>
public interface ILoopbackHttpListenerFactory
{
    /// <summary>Creates a loopback listener with the specified timeout.</summary>
    ILoopbackHttpListener CreateListener(TimeSpan timeout, int preferredPort = 0);
}
