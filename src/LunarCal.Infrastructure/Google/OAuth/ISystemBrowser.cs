using System.Diagnostics;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Abstraction for opening the system's default browser (FR-AUTH-1, SEC-OAUTH-1).
/// </summary>
public interface ISystemBrowser
{
    /// <summary>
    /// Opens the specified URI in the system default web browser.
    /// </summary>
    Task OpenBrowserAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Launches the system default web browser via Windows shell execution (FR-AUTH-1).
/// No embedded WebView is used.
/// </summary>
public sealed class SystemBrowser : ISystemBrowser
{
    /// <inheritdoc />
    public Task OpenBrowserAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var psi = new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }
}
