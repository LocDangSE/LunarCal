using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.Tests.TestDoubles;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>Signing in with another Google account: confirm first, then replace (FR-AUTH-5, ADR-0029).</summary>
public sealed class AccountSwitchViewModelTests
{
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly IAccountSessionService _session = Substitute.For<IAccountSessionService>();
    private readonly ISyncScheduler _scheduler = Substitute.For<ISyncScheduler>();
    private readonly IAccountSwitchPrompt _prompt = Substitute.For<IAccountSwitchPrompt>();

    private static readonly GoogleUserInfo _newUser = new() { Id = "new-2", Email = "new@example.com", DisplayName = "New Person" };

    private static readonly AccountSignInAssessment _different = new()
    {
        Kind = AccountSignInKind.DifferentAccount,
        CurrentAccount = new AccountInfo { Id = "old-1", Email = "old@example.com" },
        IncomingAccount = new AccountInfo { Id = "new-2", Email = "new@example.com" },
        PendingChanges = 3,
        PendingAiCommands = 2,
        LocalLunarRules = 4,
    };

    public AccountSwitchViewModelTests()
    {
        _oauth.AuthorizeAsync(Arg.Any<CancellationToken>()).Returns(GoogleAuthResult.Succeeded(
            _newUser, new GoogleOAuthTokens { AccessToken = "a", TokenType = "Bearer", RefreshToken = "r" }));
    }

    private GoogleAccountViewModel AccountPage(IAccountSwitchPrompt? prompt) =>
        new(_oauth, Substitute.For<IGoogleSignOutService>(), _accounts, _session, _loc, _scheduler, switchPrompt: prompt);

    /// <summary>Makes the session service behave like a stored different account: it asks the delegate, then reports <paramref name="outcome"/>.</summary>
    private void SessionAsks(AccountSignInOutcome outcome, Action<bool> sawAnswer)
    {
        _session.ApplySignInAsync(Arg.Any<GoogleUserInfo>(), Arg.Any<Func<AccountSignInAssessment, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var confirm = call.ArgAt<Func<AccountSignInAssessment, CancellationToken, Task<bool>>>(1);
                sawAnswer(await confirm(_different, CancellationToken.None));
                return outcome;
            });
    }

    [Fact]
    public async Task FR_AUTH_5_Confirmed_switch_shows_the_new_account_and_syncs_it()
    {
        var ct = TestContext.Current.CancellationToken;
        _prompt.ConfirmSwitchAsync(_different).Returns(true);
        bool? answer = null;
        SessionAsks(AccountSignInOutcome.Switched, a => answer = a);
        var vm = AccountPage(_prompt);
        var changed = false;
        vm.AccountChanged += (_, _) => changed = true;

        await vm.SignInAsync(ct);

        answer.Should().BeTrue("the dialog's answer reaches the session service");
        vm.IsSignedIn.Should().BeTrue();
        vm.AccountId.Should().Be("new-2");
        vm.AccountEmail.Should().Be("new@example.com");
        changed.Should().BeTrue();
        await _scheduler.Received(1).TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Declined_switch_keeps_the_current_account_and_does_not_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        _prompt.ConfirmSwitchAsync(_different).Returns(false);
        _accounts.GetPrimaryAccountAsync(Arg.Any<CancellationToken>()).Returns(new AccountInfo { Id = "old-1", Email = "old@example.com" });
        bool? answer = null;
        SessionAsks(AccountSignInOutcome.Cancelled, a => answer = a);
        var vm = AccountPage(_prompt);
        await vm.LoadAccountAsync(ct);
        var changed = false;
        vm.AccountChanged += (_, _) => changed = true;

        await vm.SignInAsync(ct);

        answer.Should().BeFalse();
        vm.AccountId.Should().Be("old-1", "declining leaves the stored account in place");
        vm.AccountEmail.Should().Be("old@example.com");
        vm.StatusMessage.Should().Be(_loc["settings.account.switch_cancelled"]);
        changed.Should().BeFalse();
        await _scheduler.DidNotReceive().TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Without_a_prompt_the_switch_is_declined_so_data_is_never_deleted_unasked()
    {
        var ct = TestContext.Current.CancellationToken;
        bool? answer = null;
        SessionAsks(AccountSignInOutcome.Cancelled, a => answer = a);
        var vm = AccountPage(prompt: null);

        await vm.SignInAsync(ct);

        answer.Should().BeFalse();
    }

    [Fact]
    public void FR_AUTH_5_The_sign_in_button_offers_to_change_the_account_once_one_is_connected()
    {
        var vm = AccountPage(_prompt);

        vm.IsSignedIn = false;
        vm.SignInButtonText.Should().Be(_loc["settings.account.signin_button"]);

        vm.IsSignedIn = true;
        vm.SignInButtonText.Should().Be(_loc["settings.advanced.change_account"]);
    }

    [Fact]
    public async Task FR_AUTH_5_First_run_does_not_finish_sign_in_when_the_switch_is_declined()
    {
        var ct = TestContext.Current.CancellationToken;
        _prompt.ConfirmSwitchAsync(_different).Returns(false);
        SessionAsks(AccountSignInOutcome.Cancelled, _ => { });
        var vm = new FirstRunViewModel(
            Substitute.For<ISettingsStore>(), new TestDisplayPreferences(), _loc, _oauth, _accounts, _session, _scheduler, switchPrompt: _prompt);

        await vm.SignInAsync(ct);

        vm.IsSignedIn.Should().BeFalse();
        vm.ErrorMessage.Should().Be(_loc["settings.account.switch_cancelled"]);
        await _scheduler.DidNotReceive().TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Re_login_in_the_main_window_keeps_the_banner_when_the_switch_is_declined()
    {
        var ct = TestContext.Current.CancellationToken;
        SessionAsks(AccountSignInOutcome.Cancelled, _ => { });
        using var vm = MainWindow();
        vm.IsReauthBannerVisible = true;

        await vm.ReauthAsync(ct);

        vm.IsReauthBannerVisible.Should().BeTrue("nothing changed: the old account still needs to sign in again");
        await _scheduler.DidNotReceive().TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Re_login_in_the_main_window_with_the_same_account_hides_the_banner_and_syncs()
    {
        var ct = TestContext.Current.CancellationToken;
        _session.ApplySignInAsync(Arg.Any<GoogleUserInfo>(), Arg.Any<Func<AccountSignInAssessment, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(AccountSignInOutcome.Saved);
        _accounts.GetPrimaryAccountAsync(Arg.Any<CancellationToken>()).Returns(new AccountInfo { Id = "new-2", Email = "new@example.com" });
        using var vm = MainWindow();
        vm.IsReauthBannerVisible = true;

        await vm.ReauthAsync(ct);

        vm.IsReauthBannerVisible.Should().BeFalse();
        await _scheduler.Received(1).TriggerManualSyncAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_5_Re_login_without_the_session_service_opens_settings_instead_of_saving_an_account()
    {
        var ct = TestContext.Current.CancellationToken;
        using var vm = new MainWindowViewModel(
            Substitute.For<IEventStore>(), _loc, Substitute.For<IThemeService>(), Substitute.For<ISettingsStore>(), new TestDisplayPreferences(),
            accountStore: _accounts, oauthClient: _oauth);
        var opened = 0;
        vm.SettingsRequested += (_, _) => opened++;

        await vm.ReauthAsync(ct);

        opened.Should().Be(1);
        await _accounts.DidNotReceive().SaveAccountAsync(Arg.Any<AccountInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FR_AUTH_5_The_dialog_lists_only_the_data_that_would_really_be_lost()
    {
        var vm = new AccountSwitchDialogViewModel(_different, _loc);

        vm.Subtitle.Should().Contain("old@example.com").And.Contain("new@example.com");
        vm.Body.Should().Contain("old@example.com").And.Contain("new@example.com");
        vm.HasWarning.Should().BeTrue();
        vm.WarningLines.Should().HaveCount(3);
        vm.WarningLines[0].Should().Contain("3");
        vm.WarningLines[1].Should().Contain("2");
        vm.WarningLines[2].Should().Contain("4");

        var onlyCache = new AccountSwitchDialogViewModel(_different with { PendingChanges = 0, PendingAiCommands = 0, LocalLunarRules = 1 }, _loc);
        onlyCache.WarningLines.Should().ContainSingle().Which.Should().Contain("1");

        var nothing = new AccountSwitchDialogViewModel(_different with { PendingChanges = 0, PendingAiCommands = 0, LocalLunarRules = 0 }, _loc);
        nothing.HasWarning.Should().BeFalse("with nothing but the re-downloadable cache to lose, the warning box is hidden");
    }

    private MainWindowViewModel MainWindow() => new(
        Substitute.For<IEventStore>(), _loc, Substitute.For<IThemeService>(), Substitute.For<ISettingsStore>(), new TestDisplayPreferences(),
        syncScheduler: _scheduler, accountStore: _accounts, oauthClient: _oauth, accountSession: _session, switchPrompt: _prompt);
}
