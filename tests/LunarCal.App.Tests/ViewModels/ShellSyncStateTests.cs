using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Infrastructure.Storage;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// P3-REVIEW (P3-R1): the shell reflects the real account and sync state (FR-UI-10, FR-AUTH-3, FR-SYNC-3) and the
/// sidebar calendar toggle is persisted (FR-UI-1). Before the fix the sync card always read "Synced · Just now",
/// the re-authentication banner never appeared and the toggle was lost.
/// </summary>
public sealed class ShellSyncStateTests
{
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);

    private MainWindowViewModel Create(IEventStore store, IAccountStore accounts, ISyncScheduler scheduler) =>
        new(store, _loc, Substitute.For<IThemeService>(), Substitute.For<ISettingsStore>(), new TestDisplayPreferences(),
            syncScheduler: scheduler, accountStore: accounts);

    private static IAccountStore Accounts(AccountInfo? account)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.GetPrimaryAccountAsync(Arg.Any<CancellationToken>()).Returns(account);
        return accounts;
    }

    [Fact]
    public async Task FR_UI_10_Signed_out_shell_says_not_connected_and_sync_now_opens_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = Substitute.For<ISyncScheduler>();
        using var vm = Create(new InMemoryEventStore(seedSampleData: false), Accounts(null), scheduler);
        await vm.LoadAccountAsync(ct);
        var settingsOpened = false;
        vm.SettingsRequested += (_, _) => settingsOpened = true;

        vm.IsSignedIn.Should().BeFalse();
        vm.SyncTitleText.Should().Be("Chưa kết nối Google Calendar");
        vm.SyncStatusText.Should().Be("Bấm để đăng nhập");

        await vm.SyncNowAsync(ct);

        settingsOpened.Should().BeTrue();
        await scheduler.DidNotReceive().TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_UI_10_Signed_in_shell_shows_never_synced_until_the_first_pass()
    {
        var ct = TestContext.Current.CancellationToken;
        using var vm = Create(new InMemoryEventStore(seedSampleData: false), Accounts(new AccountInfo { Id = "a", Email = "me@gmail.com", DisplayName = "Lộc Đặng" }), Substitute.For<ISyncScheduler>());
        await vm.LoadAccountAsync(ct);

        vm.IsSignedIn.Should().BeTrue();
        vm.UserInitials.Should().Be("LĐ");
        vm.SyncTitleText.Should().Be(_loc["sidebar.sync_title"]);
        vm.SyncStatusText.Should().Be("Chưa đồng bộ");
    }

    [Fact]
    public async Task FR_AUTH_3_Sync_needing_reauthentication_shows_the_banner_and_error_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = Substitute.For<ISyncScheduler>();
        using var vm = Create(new InMemoryEventStore(seedSampleData: false), Accounts(new AccountInfo { Id = "a", Email = "me@gmail.com" }), scheduler);
        await vm.LoadAccountAsync(ct);

        await vm.ApplySyncResultAsync(SyncResult.Failed("invalid_grant", requiresReauth: true), ct);

        vm.IsReauthBannerVisible.Should().BeTrue();
        vm.IsSyncError.Should().BeTrue();
        vm.SyncTitleText.Should().Be(_loc["sidebar.sync_title_failed"]);

        scheduler.LastSyncTime.Returns(DateTimeOffset.UtcNow);
        await vm.ApplySyncResultAsync(SyncResult.Succeeded(1, 0), ct);

        vm.IsReauthBannerVisible.Should().BeFalse();
        vm.IsSyncError.Should().BeFalse();
        vm.SyncStatusText.Should().Be(_loc["time.just_now"]);
    }

    [Fact]
    public async Task FR_SYNC_1_Successful_sync_reloads_sidebar_calendars_from_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);
        using var vm = Create(store, Accounts(new AccountInfo { Id = "a", Email = "me@gmail.com" }), Substitute.For<ISyncScheduler>());
        await vm.LoadAccountAsync(ct);
        await vm.LoadCalendarsAsync(ct);
        vm.Calendars.Should().BeEmpty();

        await store.UpsertCalendarAsync(new CalendarInfo { Id = "me@gmail.com", AccountId = "a", Summary = "Cá nhân" }, ct);
        await vm.ApplySyncResultAsync(SyncResult.Succeeded(1, 0), ct);

        vm.Calendars.Select(c => c.Summary).Should().Equal("Cá nhân");
    }

    [Fact]
    public async Task FR_UI_1_Sidebar_toggle_is_persisted_in_the_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryEventStore(seedSampleData: false);
        await store.UpsertCalendarAsync(new CalendarInfo { Id = "work", AccountId = "a", Summary = "Work" }, ct);
        using var vm = Create(store, Accounts(new AccountInfo { Id = "a", Email = "me@gmail.com" }), Substitute.For<ISyncScheduler>());
        await vm.LoadCalendarsAsync(ct);

        vm.ToggleCalendarCommand.Execute(vm.Calendars[0]);

        var persisted = false;
        for (var i = 0; i < 50 && !persisted; i++)
        {
            persisted = (await store.GetCalendarByIdAsync("work", ct))!.IsSelected == false;
            if (!persisted)
            {
                await Task.Delay(20, ct);
            }
        }

        persisted.Should().BeTrue("unchecking a calendar must survive a restart and a sync");
    }

    [Fact]
    public void FR_SYNC_3_Window_focus_and_minimize_drive_the_scheduler()
    {
        var scheduler = Substitute.For<ISyncScheduler>();
        using var vm = Create(new InMemoryEventStore(seedSampleData: false), Accounts(null), scheduler);

        vm.OnWindowMinimizedChanged(isMinimized: true);
        scheduler.Received(1).SetState(SyncScheduleState.TrayOrBackground);

        vm.OnWindowActivated();
        scheduler.Received(1).SetState(SyncScheduleState.Active);
    }
}
