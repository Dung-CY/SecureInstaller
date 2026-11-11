using System.Security.AccessControl;
using System.Security.Principal;
using Installer.Core.Errors;

namespace Installer.Core.Platform;

/// <summary>Creates directories with a protected DACL, refusing reparse points on the path.</summary>
public static class SecureDirectory
{
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static DirectorySecurity BuildSecurity(bool elevated)
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var sids = new SecurityIdentifier?[]
        {
            admins,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            // Elevated: no user rule, so a non-elevated same-user process cannot tamper with the folder.
            elevated ? null : WindowsIdentity.GetCurrent().User,
        };
        foreach (var sid in sids.Where(s => s != null).Distinct())
        {
            security.AddAccessRule(new FileSystemAccessRule(sid!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        if (elevated) security.SetOwner(admins);
        return security;
    }

    public static void CreateProtected(string path, bool elevated)
    {
        var full = System.IO.Path.GetFullPath(path);
        try
        {
            CheckNoReparse(full);
            var di = new DirectoryInfo(full);
            if (!di.Exists) di.Create(BuildSecurity(elevated));
            CheckNoReparse(full);
            // Always (re)apply the DACL; the owner is set separately below because it needs elevation.
            di.SetAccessControl(BuildSecurity(elevated));
            if (elevated)
            {
                var owner = new DirectorySecurity();
                owner.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                try { di.SetAccessControl(owner); }
                catch { throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe); }
            }
        }
        catch (WorkspaceUnsafeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe);
        }
    }

    private static void CheckNoReparse(string full)
    {
        // Walk from the drive root down to the path itself; non-existent tail segments are skipped.
        var root = System.IO.Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var part in full.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            FileAttributes attrs;
            try { attrs = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attrs & FileAttributes.ReparsePoint) != 0) throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe);
        }
    }
}
