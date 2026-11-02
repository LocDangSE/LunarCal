using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;

namespace LunarCal.Infrastructure.Tests.Security;

public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _testDirectory;

    public DpapiSecretStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "LunarCalTests_Secrets_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore transient test cleanup failure
            }
        }
    }

    [Fact]
    public async Task SEC_KEY_1_Round_trip_persists_and_retrieves_string_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        const string key = KnownSecretKeys.GeminiApiKey;
        string secretValue = "AIza" + "SySampleSecretKeyForTestingDpapi999";

        await store.SetSecretAsync(key, secretValue, ct);

        string? retrieved = await store.GetSecretAsync(key, ct);
        retrieved.Should().Be(secretValue);
    }

    [Fact]
    public async Task SEC_KEY_1_Round_trip_persists_and_retrieves_binary_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        const string key = KnownSecretKeys.DatabaseKey;
        byte[] secretBytes = [0x01, 0x02, 0x03, 0xDE, 0xAD, 0xBE, 0xEF, 0x42];

        await store.SetSecretBytesAsync(key, secretBytes, ct);

        byte[]? retrieved = await store.GetSecretBytesAsync(key, ct);
        retrieved.Should().NotBeNull();
        retrieved.Should().Equal(secretBytes);
    }

    [Fact]
    public async Task SEC_KEY_1_Saved_files_do_not_contain_plaintext_secret_or_key()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        const string key = "test.sensitive.credential";
        const string sensitiveSecret = "SuperConfidentialSecretContentNeverToBeFoundInPlaintext!@#456";

        await store.SetSecretAsync(key, sensitiveSecret, ct);

        // Verify that the file exists on disk
        string[] datFiles = Directory.GetFiles(_testDirectory, "*.dat");
        datFiles.Should().HaveCount(1);

        byte[] rawDiskBytes = await File.ReadAllBytesAsync(datFiles[0], ct);
        string rawUtf8 = Encoding.UTF8.GetString(rawDiskBytes);
        string rawLatin1 = Encoding.Latin1.GetString(rawDiskBytes);
        string rawAscii = Encoding.ASCII.GetString(rawDiskBytes);

        // Neither the secret nor the key name should appear in plaintext anywhere in the file
        rawUtf8.Should().NotContain(sensitiveSecret);
        rawLatin1.Should().NotContain(sensitiveSecret);
        rawAscii.Should().NotContain(sensitiveSecret);

        rawUtf8.Should().NotContain(key);
        rawLatin1.Should().NotContain(key);
        rawAscii.Should().NotContain(key);

        // Filename itself should be a hash, not contain the plaintext key
        Path.GetFileName(datFiles[0]).Should().NotContain("sensitive");
    }

    [Fact]
    public async Task SEC_OAUTH_3_Refresh_token_round_trip_and_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        string key = KnownSecretKeys.GoogleRefreshToken("user@example.com");
        const string refreshToken = "1//0gExampleRefreshToken-ABC123XYZ456";

        await store.SetSecretAsync(key, refreshToken, ct);
        (await store.HasSecretAsync(key, ct)).Should().BeTrue();

        string? retrieved = await store.GetSecretAsync(key, ct);
        retrieved.Should().Be(refreshToken);

        bool deleted = await store.DeleteSecretAsync(key, ct);
        deleted.Should().BeTrue();

        (await store.HasSecretAsync(key, ct)).Should().BeFalse();
        (await store.GetSecretAsync(key, ct)).Should().BeNull();

        bool deletedAgain = await store.DeleteSecretAsync(key, ct);
        deletedAgain.Should().BeFalse();
    }

    [Fact]
    public async Task FR_AUTH_3_Refresh_token_update_overwrites_previous_value()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        string key = KnownSecretKeys.GoogleRefreshToken("user@example.com");
        const string initialToken = "initial-token-111";
        const string updatedToken = "updated-token-222";

        await store.SetSecretAsync(key, initialToken, ct);
        (await store.GetSecretAsync(key, ct)).Should().Be(initialToken);

        await store.SetSecretAsync(key, updatedToken, ct);
        (await store.GetSecretAsync(key, ct)).Should().Be(updatedToken);
    }

    [Fact]
    public async Task ClearAllAsync_Removes_all_stored_secrets_and_temp_files()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        await store.SetSecretAsync("key1", "val1", ct);
        await store.SetSecretAsync("key2", "val2", ct);
        await store.SetSecretAsync("key3", "val3", ct);

        Directory.GetFiles(_testDirectory, "*.dat").Should().HaveCount(3);

        await store.ClearAllAsync(ct);

        Directory.GetFiles(_testDirectory, "*.dat").Should().BeEmpty();
        (await store.GetSecretAsync("key1", ct)).Should().BeNull();
        (await store.GetSecretAsync("key2", ct)).Should().BeNull();
        (await store.GetSecretAsync("key3", ct)).Should().BeNull();
    }

    [Fact]
    public async Task Wrong_entropy_fails_to_decrypt_and_throws_CryptographicException()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] entropyA = [0x01, 0x02, 0x03, 0x04];
        byte[] entropyB = [0x09, 0x08, 0x07, 0x06];

        using var storeA = new DpapiSecretStore(_testDirectory, entropyA);
        await storeA.SetSecretAsync("test.key", "my-secret-content", ct);

        using var storeB = new DpapiSecretStore(_testDirectory, entropyB);
        var act = async () => await storeB.GetSecretAsync("test.key", ct);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        string? result = await store.GetSecretAsync("non.existent.key", ct);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateKey_ThrowsArgumentException_OnNullOrWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);

        var actNull = async () => await store.GetSecretAsync(null!, ct);
        var actEmpty = async () => await store.GetSecretAsync("", ct);
        var actWhitespace = async () => await store.GetSecretAsync("   ", ct);

        await actNull.Should().ThrowAsync<ArgumentException>();
        await actEmpty.Should().ThrowAsync<ArgumentException>();
        await actWhitespace.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetSecretAsync_ThrowsArgumentNullException_OnNullSecret()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new DpapiSecretStore(_testDirectory);
        var act = async () => await store.SetSecretAsync("valid.key", null!, ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
