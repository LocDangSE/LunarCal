using LunarCal.Core.Abstractions;
using Microsoft.Extensions.Options;
using Serilog;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// DelegatingHandler that enforces network security constraints (SEC-NET-1) on outbound requests:
/// HTTPS only, and allow-listed hosts only.
/// </summary>
public sealed class NetworkSecurityDelegatingHandler : DelegatingHandler
{
    private readonly NetworkOptions _options;
    private readonly ILogger _logger;

    public NetworkSecurityDelegatingHandler(
        IOptions<NetworkOptions> options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? Log.ForContext<NetworkSecurityDelegatingHandler>();
    }

    public NetworkSecurityDelegatingHandler(
        NetworkOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? Log.ForContext<NetworkSecurityDelegatingHandler>();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            NetworkSecurityValidator.ValidateRequest(request, _options);
        }
        catch (NetworkSecurityException ex)
        {
            _logger.Warning(
                "Blocked outbound HTTP request violating SEC-NET-1: Reason={Reason}, Host={Host}, Scheme={Scheme}",
                ex.Reason,
                ex.BlockedHost ?? "unknown",
                request.RequestUri?.Scheme ?? "unknown");
            throw;
        }

        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            NetworkSecurityValidator.ValidateRequest(request, _options);
        }
        catch (NetworkSecurityException ex)
        {
            _logger.Warning(
                "Blocked outbound HTTP request violating SEC-NET-1: Reason={Reason}, Host={Host}, Scheme={Scheme}",
                ex.Reason,
                ex.BlockedHost ?? "unknown",
                request.RequestUri?.Scheme ?? "unknown");
            throw;
        }

        return base.Send(request, cancellationToken);
    }
}
