using Installer.Core.Install;
using Installer.Core.Tests.Fakes;
using System.Globalization;
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

    [Theory]
    [InlineData("")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("\\x")]
    public void Marker_rejects_unsafe_ids(string id)
    {
        var reg = new FakeRegistry();
        var m = new InstallMarker(reg, reg);
        Assert.Throws<ArgumentException>(() => m.Write(id, "1", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => m.Remove(id));
    }

    [Fact]
    public void ReadAll_skips_bad_ids()
    {
        var reg = new FakeRegistry();
        var root = @"SOFTWARE\SecureInstaller\Installed\";
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, root + "good", new() { ["Version"] = "1" });
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, root + "bad id", new() { ["Version"] = "2" });
        var all = new InstallMarker(reg, reg).ReadAll();
        Assert.Equal(new[] { "good" }, all.Keys.ToArray());
    }

    [Fact]
    public void Remove_missing_id_is_noop()
    {
        var reg = new FakeRegistry();
        var m = new InstallMarker(reg, reg);
        m.Write("keep", "1", DateTimeOffset.UtcNow);
        m.Remove("missing");
        Assert.Equal("1", m.ReadAll()["keep"]);
    }

    [Fact]
    public void InstalledAt_is_iso8601()
    {
        var reg = new FakeRegistry();
        var at = new DateTimeOffset(2026, 11, 11, 8, 30, 0, TimeSpan.FromHours(7));
        new InstallMarker(reg, reg).Write("p", "1", at);
        var raw = (string)reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\SecureInstaller\Installed\p", "InstalledAt")!;
        Assert.True(DateTimeOffset.TryParseExact(raw, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var back));
        Assert.Equal(at, back);
    }
}
