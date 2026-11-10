using System.Net.NetworkInformation;
using System.ServiceProcess;

namespace Installer.Core.Platform;

public sealed class SystemInfo : ISystemInfo
{
    public string Hostname => Environment.MachineName;

    public bool IsDomainJoined
    {
        get
        {
            try { return !string.IsNullOrEmpty(IPGlobalProperties.GetIPGlobalProperties().DomainName); }
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
