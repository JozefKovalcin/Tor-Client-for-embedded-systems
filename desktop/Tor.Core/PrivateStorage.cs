using System.Security.AccessControl;
using System.Security.Principal;

namespace Tor.Core;

public static class PrivateStorage
{
    /// <summary>Create only a fresh directory; never repair permissions or overwrite pre-existing state.</summary>
    public static void CreateNewDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Private scope already exists");
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Missing Windows owner");
            var acl = new DirectorySecurity();
            acl.SetOwner(owner); acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var identity in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(acl);
        }
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RequireOwnerOnly(path);
    }

    public static void RequireOwnerOnly(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Private scope cannot contain link components");
            current = Path.GetDirectoryName(current) ?? "";
        }
        if (!OperatingSystem.IsWindows())
        {
            if ((File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new UnauthorizedAccessException("Private directory must be owner-only");
            return;
        }
        var owner = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Missing Windows owner");
        var allowed = new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) };
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Private directory owner differs from current user");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            bool trusted = false;
            foreach (var identity in allowed) if (identity.Equals(rule.IdentityReference)) trusted = true;
            if (!trusted) throw new UnauthorizedAccessException("Private directory grants access beyond owner/system/administrators");
        }
    }
}
