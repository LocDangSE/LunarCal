using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

public sealed class NetworkAndSyncStatusViewModelTests
{
    private readonly LocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly ISettingsStore _settingsStore;
    private readonly TestDisplayPreferences _preferences = new();
    private readonly IEventStore _eventStore;

    public NetworkAndSyncStatusViewModelTests()
    {
        _localizationService = new LocalizationService(SupportedCultures.Vietnamese);
        _themeService = Substitute.For<IThemeService>();
        _settingsStore = Substitute.For<ISettingsStore>();
        _eventStore = Substitute.For<IEventStore>();
    }

    private sealed class TestNetworkStatusService : INetworkStatusService
    {
        public bool IsOnline { get; private set; } = true;
        public event EventHandler<bool>? NetworkAvailabilityChanged;

        public void SetOnlineForTesting(bool isOnline)
        {
            IsOnline = isOnline;
            NetworkAvailabilityChanged?.Invoke(this, isOnline);
        }
    }

    [Fact]
    public void FR_UI_9_NetworkStatusService_updates_MainWindowViewModel_online_offline_state()
    {
        var networkService = new TestNetworkStatusService();
        using var vm = new MainWindowViewModel(
            _eventStore,
            _localizationService,
            _themeService,
            _settingsStore,
            _preferences,
            networkStatusService: networkService);

        vm.IsOnline.Should().BeTrue();
        vm.IsOfflineBannerVisible.Should().BeFalse();
        vm.NetworkStatusText.Should().Be(_localizationService["network.online"]);

        // Simulate going offline
        networkService.SetOnlineForTesting(false);
        vm.IsOnline.Should().BeFalse();
        vm.IsOfflineBannerVisible.Should().BeTrue();
        vm.NetworkStatusText.Should().Be(_localizationService["network.offline"]);

        // Dismiss banner
        vm.DismissOfflineBannerCommand.Execute(null);
        vm.IsOfflineBannerVisible.Should().BeFalse();

        // Simulate returning online
        networkService.SetOnlineForTesting(true);
        vm.IsOnline.Should().BeTrue();
        vm.IsOfflineBannerVisible.Should().BeFalse();
        vm.NetworkStatusText.Should().Be(_localizationService["network.online"]);
    }

    [Fact]
    public void FR_UI_10_SyncScheduler_events_update_MainWindowViewModel_sync_status()
    {
        var syncScheduler = Substitute.For<ISyncScheduler>();
        using var vm = new MainWindowViewModel(
            _eventStore,
            _localizationService,
            _themeService,
            _settingsStore,
            _preferences,
            syncScheduler: syncScheduler);

        vm.IsSyncing.Should().BeFalse();

        // Trigger SyncStarted
        syncScheduler.SyncStarted += Raise.Event<EventHandler>(syncScheduler, EventArgs.Empty);
        vm.IsSyncing.Should().BeTrue();
        vm.SyncStatusText.Should().Be(_localizationService["sidebar.sync_status_syncing"]);

        // Trigger SyncCompleted
        syncScheduler.LastSyncTime.Returns(DateTimeOffset.UtcNow);
        syncScheduler.SyncCompleted += Raise.Event<EventHandler<SyncResult>>(
            syncScheduler,
            SyncResult.Succeeded(1, 5));

        vm.IsSyncing.Should().BeFalse();
    }

    [Fact]
    public async Task FR_AUTH_1_GoogleAccountViewModel_SignIn_success_updates_state_and_triggers_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        var oauthClient = Substitute.For<IGoogleOAuthClient>();
        var tokens = new GoogleOAuthTokens
        {
            AccessToken = "token-123",
            TokenType = "Bearer",
            RefreshToken = "refresh-456"
        };
        var userInfo = new GoogleUserInfo
        {
            Id = "google-1",
            Email = "user@example.com",
            DisplayName = "Minh Tran"
        };
        oauthClient.AuthorizeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GoogleAuthResult.Succeeded(userInfo, tokens)));

        var accountStore = Substitute.For<IAccountStore>();
        var syncScheduler = Substitute.For<ISyncScheduler>();
        var signOutService = Substitute.For<IGoogleSignOutService>();

        var vm = new GoogleAccountViewModel(
            oauthClient,
            signOutService,
            accountStore,
            _localizationService,
            syncScheduler);

        var changedFired = false;
        vm.AccountChanged += (_, _) => changedFired = true;

        await vm.SignInAsync(ct);

        vm.IsSignedIn.Should().BeTrue();
        vm.AccountId.Should().Be("google-1");
        vm.AccountEmail.Should().Be("user@example.com");
        vm.AccountDisplayName.Should().Be("Minh Tran");
        vm.AccountInitials.Should().Be("MT");
        vm.ErrorMessage.Should().BeNull();
        changedFired.Should().BeTrue();

        await accountStore.Received(1).SaveAccountAsync(
            Arg.Is<AccountInfo>(a => a.Id == "google-1" && a.Email == "user@example.com"),
            Arg.Any<CancellationToken>());

        await syncScheduler.Received(1).TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_1_GoogleAccountViewModel_SignIn_cancelled_reports_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var oauthClient = Substitute.For<IGoogleOAuthClient>();
        oauthClient.AuthorizeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GoogleAuthResult.Failed("cancelled")));

        var accountStore = Substitute.For<IAccountStore>();
        var syncScheduler = Substitute.For<ISyncScheduler>();
        var signOutService = Substitute.For<IGoogleSignOutService>();

        var vm = new GoogleAccountViewModel(
            oauthClient,
            signOutService,
            accountStore,
            _localizationService,
            syncScheduler);

        await vm.SignInAsync(ct);

        vm.IsSignedIn.Should().BeFalse();
        vm.ErrorMessage.Should().Be(_localizationService["settings.account.auth_cancelled"]);
    }

    [Fact]
    public async Task FR_AUTH_4_GoogleAccountViewModel_SignOut_cleans_up_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountStore = Substitute.For<IAccountStore>();
        var oauthClient = Substitute.For<IGoogleOAuthClient>();
        var syncScheduler = Substitute.For<ISyncScheduler>();
        var signOutService = Substitute.For<IGoogleSignOutService>();
        signOutService.SignOutAsync("google-1", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GoogleSignOutResult.Succeeded()));

        var vm = new GoogleAccountViewModel(
            oauthClient,
            signOutService,
            accountStore,
            _localizationService,
            syncScheduler)
        {
            AccountId = "google-1",
            AccountEmail = "user@example.com",
            AccountDisplayName = "User Name",
            AccountInitials = "UN",
            IsSignedIn = true
        };

        var changedFired = false;
        vm.AccountChanged += (_, _) => changedFired = true;

        await vm.SignOutAsync(ct);

        vm.IsSignedIn.Should().BeFalse();
        vm.AccountId.Should().BeEmpty();
        vm.AccountEmail.Should().BeEmpty();
        vm.AccountInitials.Should().Be("?");
        changedFired.Should().BeTrue();

        await accountStore.Received(1).DeleteAccountAsync("google-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FR_UI_10_RelativeSyncTime_formatting_covers_all_ranges()
    {
        var loc = _localizationService;

        GoogleAccountViewModel.FormatRelativeSyncTime(null, loc)
            .Should().Be(loc["settings.account.never_synced"]);

        var justNow = DateTimeOffset.UtcNow.AddSeconds(-20);
        GoogleAccountViewModel.FormatRelativeSyncTime(justNow, loc)
            .Should().Be(loc.Format("settings.account.last_synced", loc["time.just_now"]));

        var fiveMinsAgo = DateTimeOffset.UtcNow.AddMinutes(-5);
        GoogleAccountViewModel.FormatRelativeSyncTime(fiveMinsAgo, loc)
            .Should().Be(loc.Format("settings.account.last_synced", loc.Format("time.minutes_ago", 5)));

        var twoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);
        GoogleAccountViewModel.FormatRelativeSyncTime(twoHoursAgo, loc)
            .Should().Be(loc.Format("settings.account.last_synced", loc.Format("time.hours_ago", 2)));

        var threeDaysAgo = DateTimeOffset.UtcNow.AddDays(-3);
        GoogleAccountViewModel.FormatRelativeSyncTime(threeDaysAgo, loc)
            .Should().Be(loc.Format("settings.account.last_synced", loc.Format("time.days_ago", 3)));
    }

    [Fact]
    public async Task FR_AUTH_6_AdvancedSettingsViewModel_SaveOAuth_persists_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Substitute.For<ISettingsStore>();
        var wipeService = Substitute.For<ILocalDataWipeService>();
        var vm = new AdvancedSettingsViewModel(store, _localizationService, wipeService)
        {
            ClientId = " custom-client-id ",
            ClientSecret = " custom-secret "
        };

        await vm.SaveOAuthAsync(ct);

        await store.Received(1).SetStringAsync(SettingKeys.GoogleOAuthClientId, "custom-client-id", Arg.Any<CancellationToken>());
        await store.Received(1).SetStringAsync(SettingKeys.GoogleOAuthClientSecret, "custom-secret", Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Be(_localizationService["settings.advanced.oauth_saved"]);
    }

    [Fact]
    public async Task SEC_DATA_3_AdvancedSettingsViewModel_WipeAllData_invokes_wipe_service()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Substitute.For<ISettingsStore>();
        var wipeService = Substitute.For<ILocalDataWipeService>();
        var vm = new AdvancedSettingsViewModel(store, _localizationService, wipeService);

        await vm.WipeAllDataAsync(ct);

        await wipeService.Received(1).WipeAllDataAsync(Arg.Any<CancellationToken>());
        vm.StatusMessage.Should().Be(_localizationService["settings.advanced.wipe_button"]);
    }

    [Fact]
    public async Task FR_UI_9_FR_AUTH_1_FirstRunViewModel_steps_flow()
    {
        var ct = TestContext.Current.CancellationToken;
        var oauthClient = Substitute.For<IGoogleOAuthClient>();
        var accountStore = Substitute.For<IAccountStore>();
        var syncScheduler = Substitute.For<ISyncScheduler>();
        var store = Substitute.For<ISettingsStore>();
        var displayPrefs = Substitute.For<IDisplayPreferences>();

        var vm = new FirstRunViewModel(
            store,
            displayPrefs,
            _localizationService,
            oauthClient,
            accountStore,
            syncScheduler);

        vm.CurrentStep.Should().Be(1);

        // Step 1: continue with Vietnamese and Lunar enabled
        vm.IsVietnamese = true;
        vm.IsLunarEnabled = true;
        await vm.ContinueStep1Async(ct);

        await store.Received(1).SetStringAsync(SettingKeys.UiLanguage, "vi-VN", Arg.Any<CancellationToken>());
        await displayPrefs.Received(1).SetLunarEnabledAsync(true, Arg.Any<CancellationToken>());
        vm.CurrentStep.Should().Be(2);

        // Step 2: Skip
        var finishedFired = false;
        vm.Finished += (_, _) => finishedFired = true;

        await vm.SkipAsync(ct);

        await store.Received(1).SetStringAsync(SettingKeys.FirstRunCompleted, "true", Arg.Any<CancellationToken>());
        finishedFired.Should().BeTrue();
    }

    [Fact]
    public void FR_UI_7_SettingsViewModel_SelectTab_updates_ActiveTab()
    {
        var appearance = new AppearanceSettingsViewModel(Substitute.For<ISettingsStore>(), _themeService, _localizationService, _preferences);
        var account = new GoogleAccountViewModel(Substitute.For<IGoogleOAuthClient>(), Substitute.For<IGoogleSignOutService>(), Substitute.For<IAccountStore>(), _localizationService, Substitute.For<ISyncScheduler>());
        var advanced = new AdvancedSettingsViewModel(Substitute.For<ISettingsStore>(), _localizationService, Substitute.For<ILocalDataWipeService>());

        var vm = new SettingsViewModel(appearance, account, advanced);

        vm.ActiveTab.Should().Be(SettingsTab.Appearance);
        vm.SelectTab(SettingsTab.GoogleAccount);
        vm.ActiveTab.Should().Be(SettingsTab.GoogleAccount);
        vm.SelectTab(SettingsTab.Advanced);
        vm.ActiveTab.Should().Be(SettingsTab.Advanced);
    }
}
