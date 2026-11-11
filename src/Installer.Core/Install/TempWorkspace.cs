using System.Security.AccessControl;
using System.Security.Principal;

namespace Installer.Core.Install;

/// <summary>ACL-restricted temp folder holding decrypted installers; removed on Dispose.</summary>
public sealed class TempWorkspace : IDisposable
{
    public string Path { get; }
    public bool Deleted { get; private set; }

    private TempWorkspace(string path) => Path = path;

    public static TempWorkspace Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SI_" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var sids = new SecurityIdentifier?[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            // Elevated: no user rule, so a non-elevated same-user process cannot tamper with decrypted installers.
            IsElevated() ? null : WindowsIdentity.GetCurrent().User,
        };
        foreach (var sid in sids.Where(s => s != null).Distinct())
        {
            security.AddAccessRule(new FileSystemAccessRule(sid!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        new DirectoryInfo(path).Create(security);
        return new TempWorkspace(path);
    }

    internal static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        if (Deleted) return;
        Deleted = TryDelete(Path, true);
    }

    /// <summary>Deletes a workspace directory with retries; schedules delete-on-reboot when it fails.</summary>
    private static bool TryDelete(string path, bool scheduleOnFailure)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return true;
                if (IsReparsePoint(path))
                {
                    Directory.Delete(path, false); // removes only the link
                    return true;
                }
                ClearAttributes(path);
                Directory.Delete(path, true);
                return true;
            }
            catch { }
            if (attempt < 2) Thread.Sleep(500);
        }
        if (!Directory.Exists(path)) return true;
        if (scheduleOnFailure) ScheduleDeleteOnReboot(path);
        return false;
    }

    private static readonly EnumerationOptions Recurse = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static void ClearAttributes(string path)
    {
        if (IsReparsePoint(path)) return;
        try
        {
            foreach (var e in Directory.EnumerateFileSystemEntries(path, "*", Recurse))
            {
                try { File.SetAttributes(e, FileAttributes.Normal); } catch { }
            }
            File.SetAttributes(path, FileAttributes.Directory);
        }
        catch { }
    }

    private static void ScheduleDeleteOnReboot(string path)
    {
        try
        {
            if (IsReparsePoint(path)) { NativePaths.DeleteOnReboot(path); return; }
            var entries = Directory.EnumerateFileSystemEntries(path, "*", Recurse).ToList();
            foreach (var f in entries.Where(File.Exists)) NativePaths.DeleteOnReboot(f);
            foreach (var d in entries.Where(Directory.Exists).OrderByDescending(x => x.Length))
                NativePaths.DeleteOnReboot(d);
            NativePaths.DeleteOnReboot(path);
        }
        catch { }
    }

    private static readonly System.Text.RegularExpressions.Regex WorkspaceName =
        new("^SI_[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsWorkspaceName(string name) => name == "SI_docs" || WorkspaceName.IsMatch(name);

    /// <summary>Removes stale %TEMP%\SI_* workspaces older than the given age. Never throws.</summary>
    public static int SweepStale(TimeSpan olderThan)
    {
        int count = 0;
        try
        {
            var cutoff = DateTime.UtcNow - olderThan;
            foreach (var d in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(), "SI_*"))
            {
                try
                {
                    if (!IsWorkspaceName(System.IO.Path.GetFileName(d))) continue;
                    if (Directory.GetLastWriteTimeUtc(d) < cutoff && TryDelete(d, false)) count++;
                }
                catch { }
            }
        }
        catch { }
        return count;
    }
}
