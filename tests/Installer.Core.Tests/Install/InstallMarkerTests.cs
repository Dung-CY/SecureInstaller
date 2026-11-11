using Installer.Core.Install;
using Installer.Core.Tests.Fakes;
using Microsoft.Win32;

namespace Installer.Core.Tests.Install;

public class InstallMarkerTests
{
    [Fact]
    public void Marker_roundtrip_write_read_remove()
    {
        var reg = new FakeRegistry();
        var m = new InstallMarker(reg, reg);
        m.Write("p1", "1.2", DateTimeOffset.UtcNow);
        m.Write("p2", "3.0", DateTimeOffset.UtcNow);
        var all = m.ReadAll();
        Assert.Equal("1.2", all["p1"]);
        Assert.Equal("3.0", all["p2"]);
        Assert.NotNull(reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\SecureInstaller\Installed\p1", "InstalledAt"));
        m.Remove("p1");
        all = m.ReadAll();
        Assert.False(all.ContainsKey("p1"));
        Assert.Equal("3.0", all["p2"]);
    }
}
