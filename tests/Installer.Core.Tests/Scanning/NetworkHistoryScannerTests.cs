using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;
using Microsoft.Win32;

namespace Installer.Core.Tests.Scanning;

public class NetworkHistoryScannerTests
{
    private const string Root = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";

    private static byte[] St(int y, int mo, int d, int h, int mi, int s)
    {
        var b = new byte[16];
        ushort[] f = { (ushort)y, (ushort)mo, 0, (ushort)d, (ushort)h, (ushort)mi, (ushort)s, 0 };
        for (int i = 0; i < 8; i++) BitConverter.GetBytes(f[i]).CopyTo(b, i * 2);
        return b;
    }

    private static void Add(FakeRegistry r, string guid, object? name, object? type, object? created, object? last)
    {
        var v = new Dictionary<string, object?> { ["ProfileName"] = name, ["NameType"] = type, ["DateCreated"] = created, ["DateLastConnected"] = last };
        r.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, Root + "\\" + guid, v);
    }

    [Fact]
    public void ParseSystemTime_decodes_bytes()
    {
        var b = new byte[] { 0xEA, 0x07, 0x0B, 0, 2, 0, 0x0A, 0, 0x0E, 0, 0x1E, 0, 0, 0, 0, 0 };
        var dt = NetworkHistoryScanner.ParseSystemTime(b);
        Assert.Equal(new DateTime(2026, 11, 10, 14, 30, 0, DateTimeKind.Local), dt);
        Assert.Equal(DateTimeKind.Local, dt!.Value.Kind);
    }

    [Fact]
    public void ParseSystemTime_wrong_length_or_type_returns_null()
    {
        Assert.Null(NetworkHistoryScanner.ParseSystemTime(new byte[8]));
        Assert.Null(NetworkHistoryScanner.ParseSystemTime(5));
        Assert.Null(NetworkHistoryScanner.ParseSystemTime(null));
        Assert.Null(NetworkHistoryScanner.ParseSystemTime(new byte[16]));
        Assert.Null(NetworkHistoryScanner.ParseSystemTime(St(2026, 13, 40, 0, 0, 0)));
    }

    [Fact]
    public async Task Scan_maps_kinds_and_sorts_by_last_connected()
    {
        var r = new FakeRegistry();
        Add(r, "{1}", "LAN", 6, St(2025, 1, 1, 0, 0, 0), St(2026, 5, 1, 0, 0, 0));
        Add(r, "{2}", "Wi-Fi", 71u, St(2025, 1, 1, 0, 0, 0), St(2026, 11, 1, 0, 0, 0));
        Add(r, "{3}", "VPN", "23", St(2025, 1, 1, 0, 0, 0), St(2026, 8, 1, 0, 0, 0));
        Add(r, "{4}", "Weird", 99L, null, St(2026, 1, 1, 0, 0, 0));
        var probe = new FakeProbe(true);
        var h = await new NetworkHistoryScanner(r, probe).ScanAsync(default);
        Assert.Equal(new[] { "Wi-Fi", "VPN", "LAN", "Weird" }, h.Profiles.Select(p => p.Name));
        Assert.Equal(NetworkKind.Wireless, h.Profiles[0].Kind);
        Assert.Equal(NetworkKind.Vpn, h.Profiles[1].Kind);
        Assert.Equal(NetworkKind.Wired, h.Profiles[2].Kind);
        Assert.Equal(NetworkKind.Other, h.Profiles[3].Kind);
        Assert.True(h.InternetNow);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Profile_with_missing_dates_is_kept_with_nulls()
    {
        var r = new FakeRegistry();
        Add(r, "{1}", "NoDates", 6, null, null);
        Add(r, "{2}", "Dated", 6, null, St(2026, 1, 1, 0, 0, 0));
        Add(r, "{3}", null, 6, null, null);
        Add(r, "{4}", "", 6, null, null);
        Add(r, "{5}", 42, 6, null, null);
        var h = await new NetworkHistoryScanner(r, new FakeProbe()).ScanAsync(default);
        Assert.Equal(new[] { "Dated", "NoDates" }, h.Profiles.Select(p => p.Name));
        Assert.Null(h.Profiles[1].LastConnected);
        Assert.Null(h.Profiles[1].FirstConnected);
    }

    [Fact]
    public async Task Empty_registry_returns_empty_list_and_probe_value()
    {
        var h = await new NetworkHistoryScanner(new FakeRegistry(), new FakeProbe(true)).ScanAsync(default);
        Assert.Empty(h.Profiles);
        Assert.True(h.InternetNow);
        h = await new NetworkHistoryScanner(new FakeRegistry(), new FakeProbe(false)).ScanAsync(default);
        Assert.False(h.InternetNow);
    }
}
