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
/// Unit tests for <see cref="FirstRunViewModel"/> Step 3 (Gemini API Key, Figma S14 39:395, FR-BYOK-1..3).
/// </summary>
public sealed class FirstRunViewModelAiStepTests
{
    private static readonly string[] _mockModels = ["gemini-3.8-flash"];

    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly IDisplayPreferences _prefs = Substitute.For<IDisplayPreferences>();
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly IAccountSessionService _session = Substitute.For<IAccountSessionService>();
    private readonly LocalizationService _loc = new(SupportedCultures.Vietnamese);
    private readonly IAiKeyService _aiKeyService = Substitute.For<IAiKeyService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();

    private FirstRunViewModel Create() => new(
        _store,
        _prefs,
        _loc,
        _oauth,
        _accounts,
        _session,
        aiKeyService: _aiKeyService,
        clipboardService: _clipboard);

    [Fact]
    public void FR_BYOK_1_Navigation_between_steps_1_2_and_3()
    {
        var vm = Create();
        vm.CurrentStep.Should().Be(1);

        // Step 1 -> Step 2
        vm.ContinueStep1Command.Execute(null);
        vm.CurrentStep.Should().Be(2);

        // Step 2 -> Step 3
        vm.SkipStep2Command.Execute(null);
        vm.CurrentStep.Should().Be(3);
        vm.StepLabel.Should().Be(_loc.Format("firstrun.step_of", 3, 3));

        // Step 3 Back -> Step 2
        vm.BackStep3Command.Execute(null);
        vm.CurrentStep.Should().Be(2);
    }

    [Fact]
    public async Task FR_BYOK_1_SaveAiKeyAndCompleteAsync_valid_key_saves_and_finishes()
    {
        var ct = TestContext.Current.CancellationToken;
        const string rawKey = "AIzaSyD_my_valid_first_run_key_1234";

        _aiKeyService.ValidateAndSaveKeyAsync(rawKey, false, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Success(_mockModels));
        _clipboard.ClearIfMatches(rawKey).Returns(true);

        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;

        var success = await vm.SaveAiKeyAndCompleteAsync(rawKey, false, ct);

        success.Should().BeTrue();
        finished.Should().BeTrue();
        await _store.Received(1).SetStringAsync(SettingKeys.FirstRunCompleted, "true", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_BYOK_1_SaveAiKeyAndCompleteAsync_empty_key_finishes_without_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;

        var success = await vm.SaveAiKeyAndCompleteAsync(string.Empty, false, ct);

        success.Should().BeTrue();
        finished.Should().BeTrue();
        await _aiKeyService.DidNotReceive().ValidateAndSaveKeyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SEC_KEY_6_SaveAiKeyAndCompleteAsync_invalid_key_shows_error_and_does_not_finish()
    {
        var ct = TestContext.Current.CancellationToken;
        const string badKey = "invalid_gemini_key_9999";

        _aiKeyService.ValidateAndSaveKeyAsync(badKey, false, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Failed(KeyValidationStatus.InvalidKey, "Unauthorized"));

        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;

        var success = await vm.SaveAiKeyAndCompleteAsync(badKey, false, ct);

        success.Should().BeFalse();
        finished.Should().BeFalse();
        vm.AiErrorMessage.Should().Be(_loc["settings.ai.err_invalid"]);
        await _store.DidNotReceive().SetStringAsync(SettingKeys.FirstRunCompleted, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
