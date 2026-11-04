using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Calendar;
using LunarCal.Core.Settings;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// P3-REVIEW (P3-R3): first run wizard rebuilt from Figma S14 (39:280, 39:336). The language card describes the
/// week start and clock it applies (FR-I18N-1, FR-UI-7); signing in keeps the wizard open on a "connected" state
/// until "Tiếp tục" (FR-AUTH-1); "Huỷ" stops waiting without an error.
/// </summary>
public sealed class FirstRunViewModelTests
{
    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly IDisplayPreferences _prefs = Substitute.For<IDisplayPreferences>();
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);

    private FirstRunViewModel Create() => new(_store, _prefs, _loc, _oauth, _accounts);

    [Fact]
    public async Task FR_I18N_1_FR_UI_7_Choosing_English_applies_the_defaults_its_card_describes()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();
        vm.StepLabel.Should().Be(_loc.Format("firstrun.step_of", 1, 3));

        vm.SelectLanguage("en");
        vm.EnglishDescription.Should().Contain("week starts Sunday").And.Contain("12-hour");
        await vm.ContinueStep1Async(ct);

        await _store.Received(1).SetStringAsync(SettingKeys.UiLanguage, "en-US", Arg.Any<CancellationToken>());
        await _prefs.Received(1).SetWeekStartAsync(DayOfWeek.Sunday, Arg.Any<CancellationToken>());
        await _prefs.Received(1).SetTimeFormatAsync("12h", Arg.Any<CancellationToken>());
        vm.CurrentStep.Should().Be(2);
        vm.StepLabel.Should().Be(_loc.Format("firstrun.step_of", 2, 3));
    }

    [Fact]
    public async Task FR_I18N_1_Choosing_Vietnamese_applies_Monday_and_24_hour()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();

        vm.SelectLanguage("vi");
        await vm.ContinueStep1Async(ct);

        await _prefs.Received(1).SetWeekStartAsync(DayOfWeek.Monday, Arg.Any<CancellationToken>());
        await _prefs.Received(1).SetTimeFormatAsync("24h", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_AUTH_1_Signing_in_saves_the_account_and_waits_for_continue()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.AuthorizeAsync(Arg.Any<CancellationToken>()).Returns(GoogleAuthResult.Succeeded(
            new GoogleUserInfo { Id = "acc", Email = "me@gmail.com" },
            new GoogleOAuthTokens { AccessToken = "a", TokenType = "Bearer" }));
        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;

        vm.FinishCommand.CanExecute(null).Should().BeFalse("Tiếp tục on step 2 needs a connected account");
        await vm.SignInAsync(ct);

        await _accounts.Received(1).SaveAccountAsync(Arg.Is<AccountInfo>(a => a.Id == "acc"), Arg.Any<CancellationToken>());
        vm.IsSignedIn.Should().BeTrue();
        vm.SignedInText.Should().Contain("me@gmail.com");
        finished.Should().BeFalse();
        vm.FinishCommand.CanExecute(null).Should().BeTrue();

        await vm.FinishAsync(ct);

        await _store.Received(1).SetStringAsync(SettingKeys.FirstRunCompleted, "true", Arg.Any<CancellationToken>());
        finished.Should().BeTrue();
    }

    [Fact]
    public async Task FR_AUTH_1_Cancelling_the_browser_wait_shows_no_error_and_back_returns_to_step_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource();
        _oauth.AuthorizeAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return GoogleAuthResult.Failed("unreachable");
        });
        var vm = Create();
        await vm.ContinueStep1Async(ct);

        var signIn = vm.SignInCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(ct);
        vm.IsAuthorizing.Should().BeTrue();
        vm.SignInCancelCommand.Execute(null);
        await signIn;

        vm.IsAuthorizing.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
        vm.IsSignedIn.Should().BeFalse();

        vm.Back();
        vm.CurrentStep.Should().Be(1);
    }
}
