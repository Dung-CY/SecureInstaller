using System.Security.AccessControl;
using System.Security.Principal;

namespace Installer.Core.Install;

/// <summary>ACL-restricted temp folder holding decrypted installers; removed on Dispose.</summary>
public sealed class TempWorkspace : IDisposable
{
    public string Path { get; }
    internal bool Deleted { get; private set; }

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
            WindowsIdentity.GetCurrent().User,
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

    public void Dispose()
    {
        if (Deleted) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(Path)) { Deleted = true; return; }
                foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(Path, true);
                Deleted = true;
                return;
            }
            catch { }
            if (attempt < 2) Thread.Sleep(500);
        }
        Deleted = !Directory.Exists(Path);
    }
}
