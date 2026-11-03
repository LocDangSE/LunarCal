using LunarCal.Core.Abstractions;

namespace LunarCal.Infrastructure.Network;

/// <summary>
/// System clock and asynchronous delay provider backed by <see cref="TimeProvider"/> (FR-SYNC-5).
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="SystemClock"/> using the specified <see cref="TimeProvider"/>.
    /// Defaults to <see cref="TimeProvider.System"/> if none is provided.
    /// </summary>
    public SystemClock(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the underlying <see cref="TimeProvider"/>.
    /// </summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc />
    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (ReferenceEquals(_timeProvider, TimeProvider.System))
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timer = _timeProvider.CreateTimer(
            _ => tcs.TrySetResult(true),
            null,
            delay,
            Timeout.InfiniteTimeSpan);

        CancellationTokenRegistration registration = default;
        try
        {
            registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }
}
