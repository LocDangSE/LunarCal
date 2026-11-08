using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Google;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Google;

public class SyncSchedulerTests
{
    private sealed class FakeClock : IClock
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource<bool>> _waitingDelays = new();

        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> RequestedDelays { get; } = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            RequestedDelays.Add(delay);

            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

            lock (_lock)
            {
                _waitingDelays.Add(tcs);
            }

            return tcs.Task;
        }

        public void AdvanceTime(TimeSpan duration)
        {
            UtcNow += duration;

            List<TaskCompletionSource<bool>> toComplete;
            lock (_lock)
            {
                toComplete = new List<TaskCompletionSource<bool>>(_waitingDelays);
                _waitingDelays.Clear();
            }

            foreach (var waiter in toComplete)
            {
                waiter.TrySetResult(true);
            }
        }
    }

    [Fact]
    public void FR_SYNC_3_Initial_state_is_active_with_2_minute_interval()
    {
        var syncEngine = Substitute.For<ISyncEngine>();
        var scheduler = new SyncScheduler(syncEngine);

        Assert.Equal(SyncScheduleState.Active, scheduler.CurrentState);
        Assert.Equal(TimeSpan.FromMinutes(2), scheduler.GetCurrentInterval());
    }

    [Fact]
    public void FR_SYNC_3_SetState_changes_interval_to_10_minutes_in_tray_mode()
    {
        var syncEngine = Substitute.For<ISyncEngine>();
        var scheduler = new SyncScheduler(syncEngine);

        SyncScheduleState? reportedState = null;
        scheduler.StateChanged += (_, state) => reportedState = state;

        scheduler.SetState(SyncScheduleState.TrayOrBackground);

        Assert.Equal(SyncScheduleState.TrayOrBackground, scheduler.CurrentState);
        Assert.Equal(SyncScheduleState.TrayOrBackground, reportedState);
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetCurrentInterval());

        // Switch back to Active
        scheduler.SetState(SyncScheduleState.Active);
        Assert.Equal(SyncScheduleState.Active, scheduler.CurrentState);
        Assert.Equal(SyncScheduleState.Active, reportedState);
        Assert.Equal(TimeSpan.FromMinutes(2), scheduler.GetCurrentInterval());
    }

    [Fact]
    public async Task FR_SYNC_3_Polling_cycle_in_active_mode_runs_every_2_minutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SyncResult.Succeeded(1, 2));

        var fakeClock = new FakeClock();
        var options = new SyncOptions { PollingActiveMinutes = 2, PollingTrayMinutes = 10 };
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            syncOptions: options,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        var firstSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.SyncCompleted += (_, _) => firstSyncTcs.TrySetResult(true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await scheduler.StartAsync(cts.Token);

        // Wait for first sync pass
        await firstSyncTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        // Verify it requested a 2-minute delay
        Assert.Contains(TimeSpan.FromMinutes(2), fakeClock.RequestedDelays);

        // Prepare waiter for 2nd sync pass
        var secondSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.SyncCompleted += (_, _) => secondSyncTcs.TrySetResult(true);

        // Advance fake clock by 2 minutes to trigger 2nd polling pass
        fakeClock.AdvanceTime(TimeSpan.FromMinutes(2));
        await secondSyncTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        await syncEngine.Received(2).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        cts.Cancel();
        await scheduler.StopAsync(ct);
    }

    [Fact]
    public async Task FR_SYNC_3_Polling_cycle_in_tray_mode_runs_every_10_minutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SyncResult.Succeeded(1, 0));

        var fakeClock = new FakeClock();
        var options = new SyncOptions { PollingActiveMinutes = 2, PollingTrayMinutes = 10 };
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            syncOptions: options,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        scheduler.SetState(SyncScheduleState.TrayOrBackground);

        var firstSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.SyncCompleted += (_, _) => firstSyncTcs.TrySetResult(true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await scheduler.StartAsync(cts.Token);

        await firstSyncTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        // Verify it requested a 10-minute delay
        Assert.Contains(TimeSpan.FromMinutes(10), fakeClock.RequestedDelays);

        var secondSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.SyncCompleted += (_, _) => secondSyncTcs.TrySetResult(true);

        // Advance 10 minutes to trigger next polling pass
        fakeClock.AdvanceTime(TimeSpan.FromMinutes(10));
        await secondSyncTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        await syncEngine.Received(2).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        cts.Cancel();
        await scheduler.StopAsync(ct);
    }

    [Fact]
    public async Task FR_SYNC_3_TriggerFocusSyncAsync_executes_immediate_sync_without_waiting()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SyncResult.Succeeded(2, 5));

        var fakeClock = new FakeClock();
        var options = new SyncOptions { PollingActiveMinutes = 2, PollingTrayMinutes = 10 };
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            syncOptions: options,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        scheduler.SetState(SyncScheduleState.TrayOrBackground);

        var firstSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.SyncCompleted += (_, _) => firstSyncTcs.TrySetResult(true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await scheduler.StartAsync(cts.Token);

        await firstSyncTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        // Now trigger immediate focus sync without advancing time
        await scheduler.TriggerFocusSyncAsync(ct);

        await syncEngine.Received(2).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        cts.Cancel();
        await scheduler.StopAsync(ct);
    }

    [Fact]
    public async Task FR_SYNC_3_Switching_windows_quickly_does_not_start_a_sync_each_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SyncResult.Succeeded(0, 0));
        var fakeClock = new FakeClock();
        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(new List<string> { "acc-1" }));

        // Ten focus changes within 20 seconds: only the first one syncs (P4-R11).
        for (var i = 0; i < 10; i++)
        {
            await scheduler.TriggerFocusSyncAsync(ct);
            fakeClock.UtcNow += TimeSpan.FromSeconds(2);
        }

        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());

        // After the minimum interval the next focus syncs again.
        fakeClock.UtcNow += TimeSpan.FromSeconds(30);
        await scheduler.TriggerFocusSyncAsync(ct);
        await syncEngine.Received(2).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_SYNC_3_TriggerPostMutationSyncAsync_executes_immediate_sync_and_raises_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        var expectedResult = SyncResult.Succeeded(1, 3);
        syncEngine.SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var fakeClock = new FakeClock();
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        SyncResult? completedResult = null;
        scheduler.SyncCompleted += (_, res) => completedResult = res;

        await scheduler.TriggerPostMutationSyncAsync(ct);

        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());
        Assert.NotNull(completedResult);
        Assert.Equal(expectedResult.EventsSynced, completedResult.EventsSynced);
        Assert.Equal(fakeClock.UtcNow, scheduler.LastSyncTime);
    }

    [Fact]
    public async Task FR_SYNC_3_TriggerManualSyncAsync_executes_immediate_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>())
            .Returns(SyncResult.Succeeded(1, 1));

        var fakeClock = new FakeClock();
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        await scheduler.TriggerManualSyncAsync(ct);

        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());
        Assert.Equal(fakeClock.UtcNow, scheduler.LastSyncTime);
    }

    [Fact]
    public async Task FR_SYNC_3_Concurrent_triggers_do_not_execute_duplicate_syncs()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        var tcs = new TaskCompletionSource<SyncResult>();

        syncEngine.SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var fakeClock = new FakeClock();
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        // Start first trigger (runs asynchronously and waits on tcs)
        var trigger1 = scheduler.TriggerManualSyncAsync(ct);
        await Task.Yield();

        Assert.True(scheduler.IsSyncInProgress);

        // Start second trigger while first is still in progress
        var trigger2 = scheduler.TriggerFocusSyncAsync(ct);
        await Task.Yield();

        // Complete the in-progress sync
        tcs.SetResult(SyncResult.Succeeded(1, 0));

        await Task.WhenAll(trigger1, trigger2);

        // SyncEngine should only have been called once because the second was coalesced/skipped
        await syncEngine.Received(1).SyncIncrementalAsync("acc-1", Arg.Any<CancellationToken>());
        Assert.False(scheduler.IsSyncInProgress);
    }

    [Fact]
    public async Task FR_SYNC_3_Sync_failure_does_not_crash_scheduler()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        syncEngine.SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SyncResult>(_ => throw new HttpRequestException("Simulated offline error"));

        var fakeClock = new FakeClock();
        var accounts = new List<string> { "acc-1" };

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(accounts));

        // Should not throw even when syncEngine throws HttpRequestException
        await scheduler.TriggerManualSyncAsync(ct);

        Assert.False(scheduler.IsSyncInProgress);
    }

    [Fact]
    public async Task FR_SYNC_3_No_accounts_configured_skips_cleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        var syncEngine = Substitute.For<ISyncEngine>();
        var fakeClock = new FakeClock();

        var scheduler = new SyncScheduler(
            syncEngine,
            connectionFactory: null,
            clock: fakeClock,
            accountIdProvider: _ => Task.FromResult(new List<string>()));

        await scheduler.TriggerManualSyncAsync(ct);

        await syncEngine.DidNotReceive().SyncIncrementalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.False(scheduler.IsSyncInProgress);
    }
}
