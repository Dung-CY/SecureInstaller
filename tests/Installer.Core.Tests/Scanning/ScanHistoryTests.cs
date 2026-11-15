using System.Text.Json;
using Installer.Core.Licensing;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Xunit.Abstractions;

namespace Installer.Core.Tests.Scanning;

public class ScanHistoryTests
{
    private readonly ITestOutputHelper _out;
    public ScanHistoryTests(ITestOutputHelper o) { _out = o; }

    private static ScanReport Report(DateTimeOffset at, int devices = 2) => new(at,
        new HardwareInfo("PC1", "Dell", "X", "SN1",
            new() { new DiskInfo("D", "s", "SCSI") }, new() { new NicInfo("n", "AA") },
            Enumerable.Range(0, devices).Select(i => new DeviceInfo("Dev " + i, "USB", @"USB\VID_" + i)).ToList(), new() { "e" }),
        new NetworkHistory(new()
        {
            new NetworkProfile("a", NetworkKind.Wired, new DateTime(2026, 1, 2, 3, 4, 5), null),
            new NetworkProfile("b", NetworkKind.Wired, null, null)
        }, true),
        new LicenseInfo("Windows", "Pro", LicenseChannel.Retail, true, "ABCDE", null, LicenseVerdict.Genuine, new() { "ok" }),
        new LicenseInfo("Office", null, LicenseChannel.Unknown, null, null, null, LicenseVerdict.NotInstalled, new()),
        new KeywordScanResult(new() { @"C:\x" }, false, false));

    private static LogEntry Entry(DateTimeOffset ts, string action, string? json) =>
        new(ts, "u", "PC1", "SN1", action, null, null, "ok", null, null, json);

    [Fact]
    public void ScanHistory_roundtrips_report_and_skips_bad_json()
    {
        var t1 = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddDays(1);
        var r1 = Report(t1);
        var json1 = JsonSerializer.Serialize(r1, Json.Options);
        var json2 = JsonSerializer.Serialize(Report(t2), Json.Options);
        var items = ScanHistory.From(new[]
        {
            Entry(t1, "scan", json1),
            Entry(t2, "scan", json2),
            Entry(t2.AddDays(1), "scan", "{not json"),
            Entry(t2.AddDays(1), "scan", null),
            Entry(t2.AddDays(1), "install", json1),
        });
        Assert.Equal(2, items.Count);
        Assert.Equal(t2, items[0].Ts);
        var back = items[1].Report;
        Assert.Equal("PC1", items[1].Hostname);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), back.Network.Profiles[0].FirstConnected);
        Assert.Null(back.Network.Profiles[1].FirstConnected);
        Assert.Equal(LicenseVerdict.Genuine, back.Windows.Verdict);
        Assert.Equal(r1.Hardware.Peripherals, back.Hardware.Peripherals);
        Assert.Equal(r1.Keywords.MatchedFiles, back.Keywords.MatchedFiles);
    }

    [Fact]
    public void One_bad_entry_of_any_kind_does_not_break_history()
    {
        var t1 = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var json = JsonSerializer.Serialize(Report(t1), Json.Options);
        var items = ScanHistory.From(new LogEntry[]
        {
            null!, // NullReferenceException, not JsonException
            Entry(t1, "scan", "\"just a string\""),
            Entry(t1, "scan", json),
        });
        Assert.Equal(t1, Assert.Single(items).Ts);
    }

    [Fact]
    public void Report_size_with_many_devices()
    {
        var json = JsonSerializer.Serialize(Report(DateTimeOffset.Now, 300), Json.Options);
        _out.WriteLine($"300 devices => {json.Length} chars");
        Assert.NotNull(JsonSerializer.Deserialize<ScanReport>(json, Json.Options));
    }

    [Fact]
    public void Old_log_entry_without_json_deserializes()
    {
        var old = """{"ts":"2026-05-01T10:00:00+00:00","username":"u","hostname":"h","bios_serial":null,"action":"scan","package_id":null,"package_version":null,"status":"ok","exit_code":null,"detail":"x"}""";
        var e = JsonSerializer.Deserialize<LogEntry>(old, Json.Options)!;
        Assert.Null(e.ScanReportJson);
        Assert.Equal("scan", e.Action);
    }

    [Fact]
    public void Line_follows_the_unit_maker_serial_mac_disks_format()
    {
        var lic = new Installer.Core.Licensing.LicenseInfo("Windows", "Pro", Installer.Core.Licensing.LicenseChannel.Retail, true, null, null,
            Installer.Core.Licensing.LicenseVerdict.Genuine, new());
        var hw = new HardwareInfo("PC-01", "Dell Inc.", "OptiPlex", "7X9K2L3",
            new() { new DiskInfo("SSD", " S4EVNX0N123 ", "SCSI"), new DiskInfo("HDD", "WD-WCC4E1", "IDE"), new DiskInfo("USB", null, "USB") },
            new()
            {
                new NicInfo("Intel Ethernet", "AA:BB:CC:00:11:22", @"PCI\VEN_8086&DEV_15BB\3&11583659&0&FE"),
                new NicInfo("Wi-Fi USB", "AA:BB:CC:00:11:33", @"USB\VID_0BDA&PID_8179\00E04C0001"),
                new NicInfo("Hyper-V Virtual Ethernet", "00:15:5D:01:02:03", @"ROOT\VMS_MP\0000"),
            }, new(), new());
        var r = new ScanReport(DateTimeOffset.Now, hw, new NetworkHistory(new(), true), lic, lic, new KeywordScanResult(new(), false, false), Unit: "LamThao");

        Assert.Equal("LamThao | Dell Inc. | 7X9K2L3 | AA:BB:CC:00:11:22;AA:BB:CC:00:11:33 | S4EVNX0N123;WD-WCC4E1", ScanHistory.Line(r));
    }
}
