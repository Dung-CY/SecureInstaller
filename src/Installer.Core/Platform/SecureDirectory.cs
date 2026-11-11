using System.Runtime.InteropServices;
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

    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System_ = new(WellKnownSidType.LocalSystemSid, null);

    internal static bool IsTrusted(DirectorySecurity sec, bool elevated)
    {
        var me = elevated ? null : WindowsIdentity.GetCurrent().User;
        bool Ok(IdentityReference? r) => r is SecurityIdentifier sid && (sid == Admins || sid == System_ || (me != null && sid == me));
        if (!Ok(sec.GetOwner(typeof(SecurityIdentifier)))) return false;
        if (!sec.AreAccessRulesProtected) return false;
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Allow && !Ok(r.IdentityReference)) return false;
        return true;
    }

    private static string? ProgramDataRoot()
    {
        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrEmpty(pd) ? null : System.IO.Path.GetFullPath(pd).TrimEnd('\\');
    }

    /// <summary>Segments to secure: every folder below %ProgramData% down to path, or just path elsewhere.</summary>
    internal static List<string> ChainSegments(string full)
    {
        var pd = ProgramDataRoot();
        var list = new List<string>();
        if (pd != null && full.StartsWith(pd + "\\", StringComparison.OrdinalIgnoreCase))
        {
            var cur = pd;
            foreach (var part in full.Substring(pd.Length + 1).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                cur = System.IO.Path.Combine(cur, part);
                list.Add(cur);
            }
        }
        else list.Add(full);
        return list;
    }

    internal static bool VerifyChain(string path, bool elevated)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            CheckNoReparse(full);
            foreach (var seg in ChainSegments(full))
            {
                if ((File.GetAttributes(seg) & FileAttributes.ReparsePoint) != 0) return false;
                if (elevated && !IsTrusted(new DirectoryInfo(seg).GetAccessControl(), true)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    public static void CreateProtected(string path, bool elevated)
    {
        var full = System.IO.Path.GetFullPath(path);
        try
        {
            CheckNoReparse(full);
            foreach (var seg in ChainSegments(full))
                SecureSegment(seg, elevated, sec => !elevated || IsTrusted(sec, true));
            CheckNoReparse(full);
        }
        catch (WorkspaceUnsafeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe);
        }
    }

    internal static void SecureSegment(string segmentPath, bool elevated, Func<DirectorySecurity, bool> trust)
    {
        CheckNoReparse(segmentPath);
        var di = new DirectoryInfo(segmentPath);
        bool exists = di.Exists;
        if (exists && !trust(di.GetAccessControl()))
        {
            // Quarantine by rename, never delete: a delete could follow attacker-planted links.
            try { Directory.Move(segmentPath, segmentPath + ".untrusted-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }
            catch { throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe); }
            di = new DirectoryInfo(segmentPath);
            exists = false;
        }
        if (elevated)
        {
            // Fail closed: a trusted existing folder is left untouched; a missing one is created exclusively
            // with our descriptor applied at creation, so there is no window where it is weakly protected.
            if (!exists) CreateExclusive(segmentPath, BuildSecurity(true));
        }
        else
        {
            if (!exists) di.Create(BuildSecurity(false));
            CheckNoReparse(segmentPath);
            di.SetAccessControl(BuildSecurity(false));
        }
        CheckNoReparse(segmentPath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string lpPathName, ref SECURITY_ATTRIBUTES lpSecurityAttributes);

    /// <summary>Creates a directory that must not already exist, applying the descriptor at creation.</summary>
    internal static void CreateExclusive(string path, DirectorySecurity security)
    {
        var sd = security.GetSecurityDescriptorBinaryForm();
        var handle = GCHandle.Alloc(sd, GCHandleType.Pinned);
        try
        {
            var sa = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = handle.AddrOfPinnedObject(),
                bInheritHandle = 0,
            };
            if (!CreateDirectoryW(path, ref sa)) throw new WorkspaceUnsafeException(Strings.WorkspaceUnsafe);
        }
        finally { handle.Free(); }
    }

    internal static void CheckNoReparse(string full)
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
