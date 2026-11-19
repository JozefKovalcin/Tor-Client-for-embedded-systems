using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Tor.Core;

public static class PrivateStorage
{
    /// <summary>Create only a fresh directory; never repair permissions or overwrite pre-existing state.</summary>
    public static void CreateNewDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Private scope already exists");
        if (OperatingSystem.IsWindows())
        {
            var identities = WindowsPrivateIdentities();
            var acl = new DirectorySecurity();
            acl.SetOwner(identities[0]); acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var identity in identities)
                acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(acl);
        }
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RequireOwnerOnly(path);
    }

    public static void RequireOwnerOnly(string path) => RequireOwnerOnly(path, directory: true);

    public static void RequireOwnerOnlyFile(string path) => RequireOwnerOnly(path, directory: false);

    public static FileStream OpenPrivateFile(string path, FileMode mode)
    {
        if (mode is not (FileMode.CreateNew or FileMode.OpenOrCreate)) throw new ArgumentOutOfRangeException(nameof(mode));
        path = Path.GetFullPath(path);
        RequireOwnerOnly(Path.GetDirectoryName(path) ?? throw new ArgumentException("Private file needs a parent", nameof(path)));
        if (mode == FileMode.OpenOrCreate)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Private file cannot be a link");
                RequireOwnerOnlyFile(path);
            }
            catch (FileNotFoundException) { }
        }
        FileStream stream;
        if (OperatingSystem.IsWindows())
        {
            var identities = WindowsPrivateIdentities();
            var acl = new FileSecurity();
            // An elevated token can otherwise create files owned by Administrators.
            acl.SetOwner(identities[0]);
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var identity in identities)
                acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
            stream = new FileInfo(path).Create(mode, FileSystemRights.Read | FileSystemRights.Write,
                FileShare.None, 4096, FileOptions.None, acl);
        }
        else stream = new FileStream(path, new FileStreamOptions
        {
            Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        try { RequireOwnerOnlyFile(path); return stream; }
        catch { stream.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier[] WindowsPrivateIdentities()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return [identity.User ?? throw new UnauthorizedAccessException("Missing Windows owner"),
            new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null)];
    }

    private static void RequireOwnerOnly(string path, bool directory)
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
        var allowed = WindowsPrivateIdentities();
        const AccessControlSections sections = AccessControlSections.Access | AccessControlSections.Owner;
        FileSystemSecurity security = directory ? new DirectoryInfo(path).GetAccessControl(sections)
            : new FileInfo(path).GetAccessControl(sections);
        if (!allowed[0].Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Private storage owner differs from current user");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            bool trusted = false;
            foreach (var identity in allowed) if (identity.Equals(rule.IdentityReference)) trusted = true;
            if (!trusted) throw new UnauthorizedAccessException("Private directory grants access beyond owner/system/administrators");
        }
    }
}
