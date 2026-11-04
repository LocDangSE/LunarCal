using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// P3-REVIEW (P3-R4): sign-out (FR-AUTH-4), wiping local data (SEC-DATA-3) and changing the OAuth client (FR-AUTH-6)
/// only run after the user confirms. Before the fix sign-out passed confirmed: true and wipe ran immediately.
/// </summary>
public sealed class ConfirmationRequiredTests
{
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);

    private static IConfirmationService Answer(bool yes)
    {
        var confirmation = Substitute.For<IConfirmationService>();
        confirmation.ConfirmAsync(Arg.Any<ConfirmationRequest>()).Returns(yes);
        return confirmation;
    }

    private GoogleAccountViewModel SignedIn(IGoogleSignOutService signOut, IConfirmationService confirmation)
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.GetPrimaryAccountAsync(Arg.Any<CancellationToken>()).Returns(new AccountInfo { Id = "acc", Email = "me@gmail.com" });
        signOut.SignOutAsync("acc", true, Arg.Any<CancellationToken>()).Returns(GoogleSignOutResult.Succeeded());
        return new GoogleAccountViewModel(Substitute.For<IGoogleOAuthClient>(), signOut, accounts, _loc, confirmation: confirmation);
    }

    [Fact]
    public async Task FR_AUTH_4_Sign_out_is_not_performed_when_the_user_cancels()
    {
        var ct = TestContext.Current.CancellationToken;
        var signOut = Substitute.For<IGoogleSignOutService>();
        var vm = SignedIn(signOut, Answer(false));
        await vm.LoadAccountAsync(ct);

        await vm.SignOutAsync(ct);

        await signOut.DidNotReceive().SignOutAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        vm.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_4_Confirmed_sign_out_revokes_and_names_the_account_in_the_prompt()
    {
        var ct = TestContext.Current.CancellationToken;
        var signOut = Substitute.For<IGoogleSignOutService>();
        var confirmation = Answer(true);
        var vm = SignedIn(signOut, confirmation);
        await vm.LoadAccountAsync(ct);

        await vm.SignOutAsync(ct);

        await confirmation.Received(1).ConfirmAsync(Arg.Is<ConfirmationRequest>(r => r.IsDestructive && r.Message.Contains("me@gmail.com")));
        await signOut.Received(1).SignOutAsync("acc", true, Arg.Any<CancellationToken>());
        vm.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task SEC_DATA_3_Wipe_runs_only_after_confirmation_and_then_asks_the_app_to_exit()
    {
        var ct = TestContext.Current.CancellationToken;
        var wipe = Substitute.For<ILocalDataWipeService>();

        var cancelled = new AdvancedSettingsViewModel(_loc, wipe, confirmation: Answer(false));
        await cancelled.WipeAllDataAsync(ct);
        await wipe.DidNotReceive().WipeAllDataAsync(Arg.Any<CancellationToken>());

        var confirmed = new AdvancedSettingsViewModel(_loc, wipe, confirmation: Answer(true));
        var exitRequested = false;
        confirmed.WipeCompleted += (_, _) => exitRequested = true;
        await confirmed.WipeAllDataAsync(ct);

        await wipe.Received(1).WipeAllDataAsync(Arg.Any<CancellationToken>());
        exitRequested.Should().BeTrue();
        confirmed.StatusMessage.Should().Be(_loc["settings.advanced.wipe_done"]);
    }

    [Fact]
    public async Task FR_AUTH_6_Saving_a_custom_client_signs_accounts_out_first_then_stores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var overrides = Substitute.For<IOAuthClientOverrideService>();
        var signOut = Substitute.For<IGoogleSignOutService>();
        var accounts = Substitute.For<IAccountStore>();
        accounts.GetAllAccountsAsync(Arg.Any<CancellationToken>()).Returns([new AccountInfo { Id = "acc", Email = "me@gmail.com" }]);
        var vm = new AdvancedSettingsViewModel(_loc, oauthOverride: overrides, confirmation: Answer(true), signOut: signOut, accounts: accounts)
        {
            ClientId = "123-abc.apps.googleusercontent.com",
            ClientSecret = "s3cret",
        };

        await vm.SaveOAuthAsync(ct);

        Received.InOrder(() =>
        {
            signOut.SignOutAsync("acc", true, Arg.Any<CancellationToken>());
            overrides.SaveAsync("123-abc.apps.googleusercontent.com", "s3cret", Arg.Any<CancellationToken>());
        });
        vm.ClientSecret.Should().BeEmpty("the typed secret is not kept in memory after saving");
    }

    [Fact]
    public async Task FR_AUTH_6_Cancelled_client_change_keeps_accounts_and_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var overrides = Substitute.For<IOAuthClientOverrideService>();
        var signOut = Substitute.For<IGoogleSignOutService>();
        var vm = new AdvancedSettingsViewModel(_loc, oauthOverride: overrides, confirmation: Answer(false), signOut: signOut, accounts: Substitute.For<IAccountStore>())
        {
            ClientId = "123-abc.apps.googleusercontent.com",
        };

        await vm.SaveOAuthAsync(ct);

        await overrides.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await signOut.DidNotReceive().SignOutAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
