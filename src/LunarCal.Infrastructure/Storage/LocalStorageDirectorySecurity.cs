using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LunarCal.Infrastructure.Storage;

/// <summary>
/// Enforces directory-level Access Control Lists (ACLs) so that application data
/// is accessible only by the current Windows user (spec §5.3, SEC-DATA-2).
/// </summary>
public static class LocalStorageDirectorySecurity
{
    /// <summary>
    /// Ensures that the specified directory exists and has ACLs configured so that only
    /// the current Windows user has <see cref="FileSystemRights.FullControl"/>,
    /// disabling inherited permissions from parent directories (SEC-DATA-2).
    /// </summary>
    /// <param name="directoryPath">The absolute path of the directory to secure.</param>
    public static void EnsureCurrentUserOnlyAcl(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ApplyCurrentUserOnlyAcl(directoryPath);
    }

    /// <summary>
    /// Checks whether the specified directory has inheritance disabled and grants access solely
    /// to the current Windows user with <see cref="FileSystemRights.FullControl"/> (SEC-DATA-2).
    /// </summary>
    /// <param name="directoryPath">The absolute path of the directory to check.</param>
    /// <returns><c>true</c> if the directory is restricted to the current user; otherwise, <c>false</c>.</returns>
    public static bool HasCurrentUserOnlyAccess(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        return CheckCurrentUserOnlyAccess(directoryPath);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyCurrentUserOnlyAcl(string directoryPath)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new InvalidOperationException("Could not determine current Windows user security identifier.");

        var directoryInfo = new DirectoryInfo(directoryPath);
        var security = new DirectorySecurity();

        // Turn off inheritance and remove all inherited rules (isProtected=true, preserveInheritance=false)
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Grant FullControl to the current user only, applying to subdirectories and files
        var accessRule = new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

        security.AddAccessRule(accessRule);
        directoryInfo.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckCurrentUserOnlyAccess(string directoryPath)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new InvalidOperationException("Could not determine current Windows user security identifier.");

        var directoryInfo = new DirectoryInfo(directoryPath);
        var security = directoryInfo.GetAccessControl(AccessControlSections.Access);

        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        if (rules.Count == 0)
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference != userSid)
            {
                return false;
            }

            if (rule.AccessControlType != AccessControlType.Allow)
            {
                return false;
            }

            if (!rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
            {
                return false;
            }
        }

        return true;
    }
}
