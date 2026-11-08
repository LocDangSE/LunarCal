using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Gemini;
using LunarCal.Infrastructure.Security;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiKeyService"/> (FR-BYOK-1..4, FR-BYOK-6, SEC-KEY-4, SEC-KEY-6..10).
/// </summary>
[Collection("SecretValueRegistry")]
public sealed class GeminiKeyServiceTests : IDisposable
{
    private static readonly string[] _mockModels = ["gemini-3.8-flash"];
    public GeminiKeyServiceTests()
    {
        SecretValueRegistry.Clear();
    }

    public void Dispose()
    {
        SecretValueRegistry.Clear();
    }

    [Theory]
    [InlineData("AIzaSyD-1234567890abcdef", "AIza••••cdef")]
    [InlineData("AIza1234", "AIza••••1234")]
    [InlineData("custom_key_for_gemini_xyz", "••••_xyz")]
    [InlineData("1234", "••••1234")]
    [InlineData("12", "••••")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void FR_BYOK_3_MaskKey_formats_correctly(string input, string expected)
    {
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        var service = new GeminiKeyService(secretStore, validator);

        var masked = service.MaskKey(input);

        masked.Should().Be(expected);
    }

    [Fact]
    public async Task FR_BYOK_1_ValidateAndSaveKeyAsync_stores_valid_key_and_registers_in_SecretValueRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        const string rawKey = "AIzaSyD_my_valid_gemini_key_1234";

        validator.ValidateKeyAsync(rawKey, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Success(_mockModels));

        var service = new GeminiKeyService(secretStore, validator);

        var result = await service.ValidateAndSaveKeyAsync(rawKey, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        await secretStore.Received(1).SetSecretAsync(KnownSecretKeys.GeminiApiKey, rawKey, Arg.Any<CancellationToken>());
        SecretValueRegistry.IsRegistered(rawKey).Should().BeTrue();
    }

    [Fact]
    public async Task SEC_KEY_6_ValidateAndSaveKeyAsync_does_not_store_key_if_validation_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        const string invalidKey = "AIzaSyD_invalid_key_9999";

        validator.ValidateKeyAsync(invalidKey, Arg.Any<CancellationToken>())
            .Returns(KeyValidationResult.Failed(KeyValidationStatus.InvalidKey, "Bad key"));

        var service = new GeminiKeyService(secretStore, validator);

        var result = await service.ValidateAndSaveKeyAsync(invalidKey, cancellationToken: ct);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(KeyValidationStatus.InvalidKey);
        await secretStore.DidNotReceive().SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        SecretValueRegistry.IsRegistered(invalidKey).Should().BeFalse();
    }

    [Fact]
    public async Task FR_BYOK_6_ValidateAndSaveKeyAsync_offline_skips_validation_and_stores_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        const string offlineKey = "AIzaSyD_offline_key_4567";

        var service = new GeminiKeyService(secretStore, validator);

        var result = await service.ValidateAndSaveKeyAsync(offlineKey, saveWithoutValidation: true, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        await validator.DidNotReceive().ValidateKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await secretStore.Received(1).SetSecretAsync(KnownSecretKeys.GeminiApiKey, offlineKey, Arg.Any<CancellationToken>());
        SecretValueRegistry.IsRegistered(offlineKey).Should().BeTrue();
    }

    [Fact]
    public async Task SEC_KEY_4_DeleteKeyAsync_removes_from_store_and_unregisters_from_registry()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        const string activeKey = "AIzaSyD_active_key_to_delete_8888";

        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(activeKey);

        SecretValueRegistry.Register(activeKey);
        SecretValueRegistry.IsRegistered(activeKey).Should().BeTrue();

        var service = new GeminiKeyService(secretStore, validator);

        await service.DeleteKeyAsync(ct);

        await secretStore.Received(1).DeleteSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>());
        SecretValueRegistry.IsRegistered(activeKey).Should().BeFalse();
    }

    [Fact]
    public async Task SEC_KEY_7_FR_BYOK_4_SuspectLeakedAsync_wipes_key_and_pauses_AI_queue()
    {
        var ct = TestContext.Current.CancellationToken;
        var secretStore = Substitute.For<ISecretStore>();
        var validator = Substitute.For<IAiKeyValidator>();
        const string leakedKey = "AIzaSyD_leaked_key_99999";

        secretStore.GetSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>())
            .Returns(leakedKey);

        var service = new GeminiKeyService(secretStore, validator);

        var eventFired = false;
        service.QueuePausedChanged += (_, paused) =>
        {
            if (paused)
            {
                eventFired = true;
            }
        };

        service.IsQueuePaused.Should().BeFalse();

        await service.SuspectLeakedAsync(ct);

        service.IsQueuePaused.Should().BeTrue();
        eventFired.Should().BeTrue();
        await secretStore.Received(1).DeleteSecretAsync(KnownSecretKeys.GeminiApiKey, Arg.Any<CancellationToken>());
        SecretValueRegistry.IsRegistered(leakedKey).Should().BeFalse();
    }
}
