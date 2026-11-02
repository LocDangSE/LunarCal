using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Infrastructure.Security;
using LunarCal.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Storage;

public sealed class LocalDataSecurityAndWipeTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    public void Dispose()
    {
        foreach (string dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Ignore transient cleanup locks
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore access issues on cleanup
            }
        }
    }

    private string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "LunarCal_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    [Fact]
    public void SEC_DATA_2_EnsureCurrentUserOnlyAcl_sets_inheritance_protection_and_restricts_to_current_user()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempDir = CreateTempDirectory();

        LocalStorageDirectorySecurity.EnsureCurrentUserOnlyAcl(tempDir);

        bool isSecure = LocalStorageDirectorySecurity.HasCurrentUserOnlyAccess(tempDir);
        isSecure.Should().BeTrue();

        using var identity = WindowsIdentity.GetCurrent();
        identity.User.Should().NotBeNull();

        var dirInfo = new DirectoryInfo(tempDir);
        var security = dirInfo.GetAccessControl(AccessControlSections.Access);

        security.AreAccessRulesProtected.Should().BeTrue("inheritance must be disabled per SEC-DATA-2");

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        rules.Count.Should().Be(1, "only the current user explicit rule must remain");

        var singleRule = rules.Cast<FileSystemAccessRule>().Single();
        singleRule.IdentityReference.Should().Be(identity.User);
        singleRule.AccessControlType.Should().Be(AccessControlType.Allow);
        singleRule.FileSystemRights.Should().HaveFlag(FileSystemRights.FullControl);
        singleRule.InheritanceFlags.Should().HaveFlag(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
    }

    [Fact]
    public void SEC_DATA_2_EnsureCurrentUserOnlyAcl_removes_inherited_permissions_from_parent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string parentDir = CreateTempDirectory();
        string subDir = Path.Combine(parentDir, "ProtectedChild");
        Directory.CreateDirectory(subDir);

        // Before securing, subfolder inherits rules from parent/temp
        var initialSecurity = new DirectoryInfo(subDir).GetAccessControl(AccessControlSections.Access);
        var initialInheritedRules = initialSecurity.GetAccessRules(includeExplicit: false, includeInherited: true, typeof(SecurityIdentifier));

        LocalStorageDirectorySecurity.EnsureCurrentUserOnlyAcl(subDir);

        var securedInfo = new DirectoryInfo(subDir);
        var securedSecurity = securedInfo.GetAccessControl(AccessControlSections.Access);

        securedSecurity.AreAccessRulesProtected.Should().BeTrue();
        var inheritedRules = securedSecurity.GetAccessRules(includeExplicit: false, includeInherited: true, typeof(SecurityIdentifier));
        inheritedRules.Count.Should().Be(0, "all inherited permissions must be stripped");

        LocalStorageDirectorySecurity.HasCurrentUserOnlyAccess(subDir).Should().BeTrue();
    }

    [Fact]
    public async Task SEC_DATA_3_WipeAllDataAsync_clears_secrets_database_and_datalake_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        string rootDir = CreateTempDirectory();
        string dbPath = Path.Combine(rootDir, "lunarcal.db");
        string secretsDir = Path.Combine(rootDir, "secrets");
        string dataLakeDir = Path.Combine(rootDir, "datalake");
        string outboxTurnDir = Path.Combine(dataLakeDir, "raw", "chat_turns", "dt=2026-11-02");
        Directory.CreateDirectory(outboxTurnDir);

        // Seed DPAPI secrets
        using var secretStore = new DpapiSecretStore(secretsDir);
        await secretStore.SetSecretAsync(KnownSecretKeys.DatabaseKey, "mock-database-encryption-key-256bit", cancellationToken);
        await secretStore.SetSecretAsync(KnownSecretKeys.GeminiApiKey, "mock-gemini-api-key-value", cancellationToken);
        await secretStore.SetSecretAsync("account:test@example.com:refresh_token", "mock-google-refresh-token", cancellationToken);

        (await secretStore.HasSecretAsync(KnownSecretKeys.DatabaseKey, cancellationToken)).Should().BeTrue();
        (await secretStore.HasSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken)).Should().BeTrue();

        // Seed mock database files
        await File.WriteAllTextAsync(dbPath, "MOCK SQLCIPHER ENCRYPTED DB CONTENT", cancellationToken);
        await File.WriteAllTextAsync(dbPath + "-wal", "MOCK WAL", cancellationToken);
        await File.WriteAllTextAsync(dbPath + "-shm", "MOCK SHM", cancellationToken);

        // Seed datalake outbox files
        string outboxRecordFile = Path.Combine(outboxTurnDir, "part-0001.jsonl");
        await File.WriteAllTextAsync(outboxRecordFile, "{\"schema_version\": 1, \"turn_id\": \"test-turn\"}", cancellationToken);

        // Seed arbitrary cache file
        string cacheFile = Path.Combine(rootDir, "query_cache.bin");
        await File.WriteAllTextAsync(cacheFile, "CACHE DATA", cancellationToken);

        File.Exists(dbPath).Should().BeTrue();
        File.Exists(outboxRecordFile).Should().BeTrue();
        File.Exists(cacheFile).Should().BeTrue();

        var wipeOptions = new LocalDataWipeOptions
        {
            RootDirectory = rootDir,
            DatabasePath = dbPath,
            SecretsDirectory = secretsDir,
            DataLakeDirectory = dataLakeDir
        };

        var wipeService = new LocalDataWipeService(secretStore, wipeOptions);

        // Execute wipe
        await wipeService.WipeAllDataAsync(cancellationToken);

        // Assert database is gone
        File.Exists(dbPath).Should().BeFalse();
        File.Exists(dbPath + "-wal").Should().BeFalse();
        File.Exists(dbPath + "-shm").Should().BeFalse();

        // Assert secrets are wiped
        (await secretStore.HasSecretAsync(KnownSecretKeys.DatabaseKey, cancellationToken)).Should().BeFalse();
        (await secretStore.HasSecretAsync(KnownSecretKeys.GeminiApiKey, cancellationToken)).Should().BeFalse();
        (await secretStore.HasSecretAsync("account:test@example.com:refresh_token", cancellationToken)).Should().BeFalse();

        // Assert datalake outbox is gone
        Directory.Exists(dataLakeDir).Should().BeFalse();

        // Assert cache files are gone
        File.Exists(cacheFile).Should().BeFalse();

        // Assert root directory maintains secure ACL
        if (OperatingSystem.IsWindows())
        {
            LocalStorageDirectorySecurity.HasCurrentUserOnlyAccess(rootDir).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SEC_DATA_3_WipeAllDataAsync_handles_non_existent_directories_without_throwing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string nonExistentRoot = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N"));

        var wipeOptions = new LocalDataWipeOptions
        {
            RootDirectory = nonExistentRoot
        };

        var wipeService = new LocalDataWipeService(secretStore: null, wipeOptions);

        var action = async () => await wipeService.WipeAllDataAsync(cancellationToken);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public void DependencyInjection_registers_ILocalDataWipeService()
    {
        var services = new ServiceCollection();
        var configuration = Substitute.For<IConfiguration>();

        services.AddLunarCalInfrastructure(configuration);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILocalDataWipeService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be<LocalDataWipeService>();
    }
}
