namespace LunarCal.Infrastructure.Google.OAuth;

/// <summary>
/// Default factory creating instances of <see cref="LoopbackHttpListener"/>.
/// </summary>
public sealed class LoopbackHttpListenerFactory : ILoopbackHttpListenerFactory
{
    /// <inheritdoc />
    public ILoopbackHttpListener CreateListener(TimeSpan timeout, int preferredPort = 0) =>
        new LoopbackHttpListener(preferredPort, timeout);
}
