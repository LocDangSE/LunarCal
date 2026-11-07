using System.Net.NetworkInformation;
using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// Service tracking network connectivity status (FR-UI-9, FR-SYNC-4, SEC-NET-1).
/// </summary>
public sealed class NetworkStatusService : INetworkStatusService, IDisposable
{
    private bool? _testingOverride;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsOnline
    {
        get
        {
            if (_testingOverride.HasValue)
            {
                return _testingOverride.Value;
            }

            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<bool>? NetworkAvailabilityChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="NetworkStatusService"/> and hooks system network change events.
    /// </summary>
    public NetworkStatusService()
    {
        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch
        {
            // Fallback gracefully on environments where network change notifications are unsupported
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (!_testingOverride.HasValue)
        {
            NetworkAvailabilityChanged?.Invoke(this, IsOnline);
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!_testingOverride.HasValue)
        {
            NetworkAvailabilityChanged?.Invoke(this, e.IsAvailable);
        }
    }

    /// <inheritdoc />
    public void SetOnlineForTesting(bool isOnline)
    {
        var changed = !_testingOverride.HasValue || _testingOverride.Value != isOnline;
        _testingOverride = isOnline;
        if (changed)
        {
            NetworkAvailabilityChanged?.Invoke(this, isOnline);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
