namespace Installer.Core.Platform;

public interface ISystemInfo
{
    string Hostname { get; }
    bool IsDomainJoined { get; }
    IReadOnlyList<string> ServiceNames();
    bool PathExists(string expandedPath);
    string? ReadHostsFile();
}
