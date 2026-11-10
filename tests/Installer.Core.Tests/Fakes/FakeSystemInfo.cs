using Installer.Core.Platform;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeSystemInfo : ISystemInfo
{
    public string Hostname { get; set; } = "TESTPC";
    public bool IsDomainJoined { get; set; }
    public List<string> Services { get; } = new();
    public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? HostsFile { get; set; }

    public IReadOnlyList<string> ServiceNames() => Services;
    public bool PathExists(string expandedPath) => ExistingPaths.Contains(expandedPath);
    public string? ReadHostsFile() => HostsFile;
}
