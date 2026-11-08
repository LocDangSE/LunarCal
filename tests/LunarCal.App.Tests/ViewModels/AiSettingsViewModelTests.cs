using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.Services;
using LunarCal.App.ViewModels;
using LunarCal.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace LunarCal.App.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="AiSettingsViewModel"/> (Figma 04, FR-BYOK-1..4, FR-BYOK-6, SEC-KEY-4, SEC-KEY-6..11).
/// </summary>
public sealed class AiSettingsViewModelTests
{
    private static readonly string[] _mockModels = ["gemini-3.8-flash"];

    private readonly IAiKeyService _aiKeyService = Substitute.For<IAiKeyService>();
    private readonly LocalizationService _localization = new(SupportedCultures.Vietnamese);
    private readonly IClipboardService _clipboardService = Substitute.For<IClipboardService>();
    private readonly INetworkStatusService _networkStatus = Substitute.For<INetworkStatusService>();

    private AiSettingsViewModel Create() => new(_aiKeyService, _localization, _clipboardService, _networkStatus);

    [Fact]
    public async Task FR_BYOK_3_InitializeAsync_loads_key_status_and_masked_key()
    {
        var ct = TestContext.Current.CancellationToken;
        _aiKeyService.HasKeyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _aiKeyService.GetMaskedKeyAsync(Arg.Any<CancellationToken>()).Returns("AIza••••1234");

        var vm = Create();
        await vm.InitializeAsync(ct);

        vm.HasKey.Should().BeTrue();
        vm.MaskedKey.Should().Be("AIza••••1234");
    }

    [Fact]
    public async Task SEC_KEY_6_CheckAndSaveKeyAsync_empty_key_sets_error_without_calling_service()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();

        var result = await vm.CheckAndSaveKeyAsync(string.Empty, ct);

        result.Should().BeFalse();
        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().NotBeNullOrEmpty();
        await _aiKeyService.DidNotReceive().ValidateAndSaveKeyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FR_BYOK_1_SEC_KEY_10_CheckAndSaveKeyAsync_success_updates_state_and_clears_clipboard_if_matches()
    {
        var ct = TestContext.Current.CancellationToken;
        const string rawKey = "AIzaSyD_my_secret_key_5678";

        _aiKeyService.ValidateAndSaveKeyAsync(rawKey, false, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Success(_mockModels));
        _aiKeyService.MaskKey(rawKey).Returns("AIza••••5678");
        _clipboardService.ClearIfMatches(rawKey).Returns(true);

        var vm = Create();
        vm.StartEditing();
        vm.IsEditing.Should().BeTrue();

        var success = await vm.CheckAndSaveKeyAsync(rawKey, ct);

        success.Should().BeTrue();
        vm.HasKey.Should().BeTrue();
        vm.MaskedKey.Should().Be("AIza••••5678");
        vm.IsEditing.Should().BeFalse();
        vm.IsStatusError.Should().BeFalse();
        vm.IsClipboardCleared.Should().BeTrue();
    }

    [Fact]
    public async Task SEC_KEY_6_CheckAndSaveKeyAsync_validation_failure_sets_error_message()
    {
        var ct = TestContext.Current.CancellationToken;
        const string badKey = "invalid_key_9999";

        _aiKeyService.ValidateAndSaveKeyAsync(badKey, false, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Failed(KeyValidationStatus.InvalidKey, "Unauthorized"));

        var vm = Create();

        var success = await vm.CheckAndSaveKeyAsync(badKey, ct);

        success.Should().BeFalse();
        vm.HasKey.Should().BeFalse();
        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Be(_localization["settings.ai.err_invalid"]);
    }

    [Fact]
    public async Task FR_BYOK_6_SaveOfflineAsync_saves_without_network_validation()
    {
        var ct = TestContext.Current.CancellationToken;
        const string offlineKey = "AIzaSyD_offline_key_1111";

        _aiKeyService.ValidateAndSaveKeyAsync(offlineKey, true, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Success());
        _aiKeyService.MaskKey(offlineKey).Returns("AIza••••1111");

        var vm = Create();

        var success = await vm.SaveOfflineAsync(offlineKey, ct);

        success.Should().BeTrue();
        vm.HasKey.Should().BeTrue();
        vm.MaskedKey.Should().Be("AIza••••1111");
        await _aiKeyService.Received(1).ValidateAndSaveKeyAsync(offlineKey, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SEC_KEY_4_DeleteKeyAsync_resets_state_and_calls_service()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();
        vm.HasKey = true;
        vm.MaskedKey = "AIza••••1234";

        await vm.DeleteKeyAsync(ct);

        vm.HasKey.Should().BeFalse();
        vm.MaskedKey.Should().BeNull();
        await _aiKeyService.Received(1).DeleteKeyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SEC_KEY_7_FR_BYOK_4_SuspectLeakedAsync_sets_leak_guide_and_calls_service()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = Create();
        vm.HasKey = true;
        vm.MaskedKey = "AIza••••1234";

        await vm.SuspectLeakedAsync(ct);

        vm.HasKey.Should().BeFalse();
        vm.MaskedKey.Should().BeNull();
        vm.IsSuspectLeaked.Should().BeTrue();
        await _aiKeyService.Received(1).SuspectLeakedAsync(Arg.Any<CancellationToken>());
    }
}
