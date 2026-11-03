namespace LunarCal.Core.Abstractions;

/// <summary>
/// Service tracking network connectivity status (FR-UI-9, FR-SYNC-4, SEC-NET-1).
/// </summary>
public interface INetworkStatusService
{
    /// <summary>
    /// Gets whether network connectivity is currently available.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Event raised when network connectivity transitions between online and offline.
    /// </summary>
    event EventHandler<bool>? NetworkAvailabilityChanged;

    /// <summary>
    /// Overrides the network availability state for testing purposes.
    /// </summary>
    /// <param name="isOnline">New network state.</param>
    void SetOnlineForTesting(bool isOnline);
}
