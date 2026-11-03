using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Loopback HTTP listener for OAuth 2.0 authorization redirects (FR-AUTH-1, SEC-OAUTH-1).
/// Strictly binds to 127.0.0.1 and closes immediately after receiving the callback or on timeout.
/// </summary>
public sealed class LoopbackHttpListener : ILoopbackHttpListener
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly string _redirectUri;
    private readonly TimeSpan _timeout;
    private int _disposed;

    /// <inheritdoc />
    public int Port => _port;

    /// <inheritdoc />
    public string RedirectUri => _redirectUri;

    /// <inheritdoc />
    public bool IsListening => _listener.IsListening;

    /// <summary>
    /// Initializes a new instance of <see cref="LoopbackHttpListener"/>.
    /// </summary>
    /// <param name="port">Specific port to bind, or 0 to automatically allocate a free port on 127.0.0.1.</param>
    /// <param name="timeout">Maximum duration to wait for redirect (SEC-OAUTH-1). Defaults to 5 minutes.</param>
    public LoopbackHttpListener(int port = 0, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        _port = port > 0 ? port : GetAvailableLoopbackPort();

        // SEC-OAUTH-1: loopback only binds 127.0.0.1
        _redirectUri = $"http://127.0.0.1:{_port}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(_redirectUri);
    }

    /// <summary>
    /// Finds a free TCP port bound strictly to 127.0.0.1.
    /// </summary>
    public static int GetAvailableLoopbackPort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    /// <inheritdoc />
    public void Start()
    {
        ThrowIfDisposed();
        if (!_listener.IsListening)
        {
            _listener.Start();
        }
    }

    /// <inheritdoc />
    public async Task<LoopbackCallbackResult> WaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Start();

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // When cancellation is triggered (either timeout or external token), abort listener immediately
        using var registration = linkedCts.Token.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Suppress exceptions during abort/stop
            }
        });

        try
        {
            var context = await _listener.GetContextAsync().ConfigureAwait(false);

            try
            {
                var query = context.Request.QueryString;
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];
                var errorDescription = query["error_description"];

                var isSuccess = string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code);
                var htmlContent = isSuccess
                    ? GetSuccessHtml()
                    : GetErrorHtml(error, errorDescription);

                var responseBytes = Encoding.UTF8.GetBytes(htmlContent);

                context.Response.StatusCode = isSuccess ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = responseBytes.Length;

                await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
                await context.Response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                context.Response.OutputStream.Close();

                return new LoopbackCallbackResult
                {
                    Code = code,
                    State = state,
                    Error = error,
                    ErrorDescription = errorDescription
                };
            }
            finally
            {
                // SEC-OAUTH-1: Close listener immediately after processing the callback
                StopListening();
            }
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            StopListening();
            throw new TimeoutException($"OAuth loopback listener timed out after {_timeout.TotalSeconds} seconds (SEC-OAUTH-1).", ex);
        }
        catch
        {
            StopListening();
            throw;
        }
    }

    /// <inheritdoc />
    public void StopListening()
    {
        if (_listener.IsListening)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Suppress exceptions when already stopped
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            StopListening();
            try
            {
                _listener.Close();
            }
            catch
            {
                // Suppress exceptions on close
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }

    private static string GetSuccessHtml() =>
        """
        <!DOCTYPE html>
        <html lang="vi">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>LunarCal - Đăng nhập thành công</title>
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                    background-color: #F9FAFB;
                    color: #101828;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100vh;
                    margin: 0;
                }
                .card {
                    background: #FFFFFF;
                    padding: 36px 44px;
                    border-radius: 12px;
                    box-shadow: 0 4px 6px -2px rgba(16, 24, 40, 0.05), 0 12px 16px -4px rgba(16, 24, 40, 0.1);
                    text-align: center;
                    max-width: 440px;
                }
                .icon {
                    font-size: 40px;
                    color: #079455;
                    margin-bottom: 16px;
                }
                h1 {
                    font-size: 20px;
                    font-weight: 600;
                    margin: 0 0 12px 0;
                    color: #101828;
                }
                p {
                    font-size: 14px;
                    line-height: 1.5;
                    color: #475467;
                    margin: 0;
                }
                .en {
                    margin-top: 8px;
                    font-size: 13px;
                    color: #98A2B3;
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon">✓</div>
                <h1>Đăng nhập thành công</h1>
                <p>Bạn có thể đóng tab này và quay lại ứng dụng <strong>LunarCal</strong>.</p>
                <p class="en">Authentication successful. You can close this tab and return to LunarCal.</p>
            </div>
        </body>
        </html>
        """;

    private static string GetErrorHtml(string? error, string? description)
    {
        var safeError = WebUtility.HtmlEncode(error ?? "unknown_error");
        var safeDesc = WebUtility.HtmlEncode(description ?? string.Empty);
        var detail = string.IsNullOrEmpty(safeDesc) ? safeError : $"{safeError}: {safeDesc}";

        return $$"""
        <!DOCTYPE html>
        <html lang="vi">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>LunarCal - Lỗi đăng nhập</title>
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                    background-color: #F9FAFB;
                    color: #101828;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    height: 100vh;
                    margin: 0;
                }
                .card {
                    background: #FFFFFF;
                    padding: 36px 44px;
                    border-radius: 12px;
                    box-shadow: 0 4px 6px -2px rgba(16, 24, 40, 0.05), 0 12px 16px -4px rgba(16, 24, 40, 0.1);
                    text-align: center;
                    max-width: 440px;
                }
                .icon {
                    font-size: 40px;
                    color: #D92D20;
                    margin-bottom: 16px;
                }
                h1 {
                    font-size: 20px;
                    font-weight: 600;
                    margin: 0 0 12px 0;
                    color: #101828;
                }
                p {
                    font-size: 14px;
                    line-height: 1.5;
                    color: #475467;
                    margin: 0;
                }
                .error-detail {
                    margin-top: 12px;
                    padding: 8px 12px;
                    background: #FEF3F2;
                    border-radius: 6px;
                    font-size: 12px;
                    color: #B42318;
                    word-break: break-all;
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon">✕</div>
                <h1>Không thể đăng nhập</h1>
                <p>Đã xảy ra lỗi khi xác thực tài khoản Google.</p>
                <div class="error-detail">{{detail}}</div>
            </div>
        </body>
        </html>
        """;
    }
}
