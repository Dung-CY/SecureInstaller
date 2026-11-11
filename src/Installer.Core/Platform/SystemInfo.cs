using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Installer.Core.Platform;

public sealed class SystemInfo : ISystemInfo
{
    public string Hostname => Environment.MachineName;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr nameBuffer, out int joinStatus);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private const int NetSetupDomainName = 3;

    public bool IsDomainJoined
    {
        get
        {
            try
            {
                var rc = NetGetJoinInformation(null, out var buf, out var status);
                if (buf != IntPtr.Zero) NetApiBufferFree(buf);
                return rc == 0 && status == NetSetupDomainName;
            }
            catch { return false; }
        }
    }

    public IReadOnlyList<string> ServiceNames()
    {
        try
        {
            var services = ServiceController.GetServices();
            try { return services.Select(s => s.ServiceName).ToList(); }
            finally { foreach (var s in services) s.Dispose(); }
        }
        catch { return Array.Empty<string>(); }
    }

    public bool PathExists(string expandedPath) => File.Exists(expandedPath) || Directory.Exists(expandedPath);

    public string? ReadHostsFile()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            return File.ReadAllText(path);
        }
        catch { return null; }
    }
}
