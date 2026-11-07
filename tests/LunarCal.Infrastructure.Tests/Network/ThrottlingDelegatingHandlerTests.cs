using System.Net;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Network;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Network;

public class ThrottlingDelegatingHandlerTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = new();
        public bool AdvanceTimeOnDelay { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            if (AdvanceTimeOnDelay)
            {
                UtcNow += delay;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CountingFakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void FR_SYNC_5_Throws_when_max_requests_per_second_is_zero_or_negative(int invalidRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThrottlingDelegatingHandler(invalidRate));
    }

    [Fact]
    public async Task FR_SYNC_5_First_N_burst_requests_proceed_immediately_without_delay()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock();
        var fakeHandler = new CountingFakeHandler();
        using var throttler = new ThrottlingDelegatingHandler(maxRequestsPerSecond: 5, clock: fakeClock, innerHandler: fakeHandler);
        using var client = new HttpMessageInvoker(throttler);

        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            using var response = await client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(5, fakeHandler.CallCount);
        Assert.Empty(fakeClock.Delays);
    }

    [Fact]
    public async Task FR_SYNC_5_Requests_enqueued_at_same_instant_are_throttled_to_one_second_window()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock { AdvanceTimeOnDelay = false };
        var fakeHandler = new CountingFakeHandler();
        using var throttler = new ThrottlingDelegatingHandler(maxRequestsPerSecond: 5, clock: fakeClock, innerHandler: fakeHandler);
        using var client = new HttpMessageInvoker(throttler);

        // Send 10 requests at the same instant
        for (int i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            using var response = await client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(10, fakeHandler.CallCount);
        // First 5 requests had 0 delay; requests 6 to 10 each received a 1.0 second delay reservation
        Assert.Equal(5, fakeClock.Delays.Count);
        foreach (var delay in fakeClock.Delays)
        {
            Assert.Equal(TimeSpan.FromSeconds(1), delay);
        }
    }

    [Fact]
    public async Task FR_SYNC_5_Continuous_flow_dispatches_at_most_5_requests_per_second()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock { AdvanceTimeOnDelay = true };
        var fakeHandler = new CountingFakeHandler();
        using var throttler = new ThrottlingDelegatingHandler(maxRequestsPerSecond: 5, clock: fakeClock, innerHandler: fakeHandler);
        using var client = new HttpMessageInvoker(throttler);

        // Send 15 requests in a continuous sequence where delay advances time
        for (int i = 0; i < 15; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            using var response = await client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(15, fakeHandler.CallCount);
        // Request 6 triggered a delay into second 1; Request 11 triggered a delay into second 2
        Assert.Equal(2, fakeClock.Delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), fakeClock.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(1), fakeClock.Delays[1]);
    }

    [Fact]
    public async Task FR_SYNC_5_Requests_spaced_more_than_one_second_apart_do_not_throttle()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock();
        var fakeHandler = new CountingFakeHandler();
        using var throttler = new ThrottlingDelegatingHandler(maxRequestsPerSecond: 5, clock: fakeClock, innerHandler: fakeHandler);
        using var client = new HttpMessageInvoker(throttler);

        // 5 requests at t = 0
        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            using var response = await client.SendAsync(request, ct);
        }

        // Advance clock past the 1-second window
        fakeClock.UtcNow += TimeSpan.FromSeconds(2);

        // 5 more requests at t = 2s
        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            using var response = await client.SendAsync(request, ct);
        }

        Assert.Equal(10, fakeHandler.CallCount);
        Assert.Empty(fakeClock.Delays);
    }

    [Fact]
    public async Task FR_SYNC_5_Cancellation_during_delay_aborts_request()
    {
        var fakeClock = new FakeClock();
        var fakeHandler = new CountingFakeHandler();
        using var throttler = new ThrottlingDelegatingHandler(maxRequestsPerSecond: 1, clock: fakeClock, innerHandler: fakeHandler);
        using var client = new HttpMessageInvoker(throttler);

        // 1st request succeeds
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList"))
        {
            await client.SendAsync(req1, CancellationToken.None);
        }

        // 2nd request has cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(req2, cts.Token));

        Assert.Equal(1, fakeHandler.CallCount);
    }

    [Fact]
    public async Task FR_SYNC_5_Works_with_custom_TimeProvider()
    {
        var initialTime = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var testTimeProvider = new TestTimeProvider(initialTime);
        var clock = new SystemClock(testTimeProvider);

        Assert.Equal(initialTime, clock.UtcNow);

        // Verify delay creates timer on custom TimeProvider
        await clock.DelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Single(testTimeProvider.TimerDueTimes);
        Assert.Equal(TimeSpan.FromMilliseconds(50), testTimeProvider.TimerDueTimes[0]);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public List<TimeSpan> TimerDueTimes { get; } = new();

        public TestTimeProvider(DateTimeOffset initialTime)
        {
            _utcNow = initialTime;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerDueTimes.Add(dueTime);
            // Fire callback synchronously for testing
            callback(state);
            return new DummyTimer();
        }

        private sealed class DummyTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
