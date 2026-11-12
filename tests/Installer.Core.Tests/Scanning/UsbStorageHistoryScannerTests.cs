using Installer.Core.Platform;
using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;
using Microsoft.Win32;

namespace Installer.Core.Tests.Scanning;

public class UsbStorageHistoryScannerTests
{
    private const string Model = "Disk&Ven_Acme&Prod_Stick&Rev_1.00";
    private const string Serial = "ABC123DEF456";

    private sealed class FakeEvents : IEventLogReader
    {
        public List<EventRecordInfo> Partition { get; } = new();
        public List<EventRecordInfo> Pnp { get; } = new();
        public IReadOnlyList<EventRecordInfo> Read(string logName, int[] ids, int max = 2000) =>
            (logName.Contains("Partition") ? Partition : Pnp).Where(e => ids.Contains(e.Id)).ToList();
    }

    private sealed class FakeFiles : IFileTextReader
    {
        public string[] Lines { get; set; } = Array.Empty<string>();
        public IEnumerable<string> ReadLines(string directory, string searchPattern) => Lines;
    }

    private static FakeRegistry Reg(params string[] instances)
    {
        var r = new FakeRegistry();
        foreach (var i in instances)
            r.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"SYSTEM\CurrentControlSet\Enum\USBSTOR\{Model}\{i}",
                new() { ["FriendlyName"] = "Acme Stick USB Device" });
        return r;
    }

    private static UsbStorageHistoryScanner Make(FakeRegistry r, FakeEvents e, FakeFiles f) => new(r, e, f, "C:\\x");

    private static string Install(string serial) =>
        $@">>>  [Device Install (Hardware initiated) - SWD\WPDBUSENUM\_??_USBSTOR#{Model}#{serial}&0#{{53f56307-b6bf-11d0-94f2-00a0c91efb8b}}]";

    [Fact]
    public void Builds_record_from_registry_setupapi_and_events()
    {
        var ev = new FakeEvents();
        ev.Partition.Add(new(1006, new DateTime(2026, 3, 2, 8, 0, 0), new object?[] { 1u, "NULL", "acme", "abc123def456" }));
        ev.Pnp.Add(new(400, new DateTime(2026, 3, 5, 9, 30, 0), new object?[] { $@"USBSTOR\{Model}\{Serial}&0", "disk.inf" }));
        ev.Pnp.Add(new(410, new DateTime(2026, 3, 4, 9, 30, 0), new object?[] { $@"USBSTOR\{Model}\{Serial}&0" }));
        var files = new FakeFiles { Lines = new[]
        {
            Install(Serial), ">>>  Section start 2026/02/10 10:00:00.123", "     ump: something",
            Install(Serial.ToLowerInvariant()), ">>>  Section start 2026/01/05 07:15:30.000",
        } };

        var rec = Assert.Single(Make(Reg(Serial + "&0"), ev, files).Scan());

        Assert.Equal("Acme Stick USB Device", rec.FriendlyName);
        Assert.Equal(Serial, rec.Serial);
        Assert.Equal($@"USBSTOR\{Model}\{Serial}&0", rec.DeviceId);
        Assert.Equal(new DateTime(2026, 1, 5, 7, 15, 30), rec.FirstConnected);
        Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 0), rec.LastConnected);
    }

    [Fact]
    public void Malformed_log_lines_are_skipped()
    {
        var files = new FakeFiles { Lines = new[]
        {
            Install(Serial), ">>>  Section start not-a-date", ">>>  Section start 2026/04/01 01:02:03.004",
            ">>>  [Device Install broken USBSTOR#", "garbage",
        } };
        var rec = Assert.Single(Make(Reg(Serial + "&0"), new FakeEvents(), files).Scan());
        Assert.Equal(new DateTime(2026, 4, 1, 1, 2, 3, 4), rec.FirstConnected);
    }

    [Fact]
    public void No_events_means_null_last_and_no_log_means_null_first()
    {
        var rec = Assert.Single(Make(Reg(Serial + "&0"), new FakeEvents(), new FakeFiles()).Scan());
        Assert.Null(rec.LastConnected);
        Assert.Null(rec.FirstConnected);
    }

    [Fact]
    public void Events_for_another_serial_are_ignored()
    {
        var ev = new FakeEvents();
        ev.Partition.Add(new(1006, DateTime.Now, new object?[] { "OTHERSERIAL" }));
        ev.Pnp.Add(new(400, DateTime.Now, new object?[] { $@"USBSTOR\{Model}\X{Serial}&0" }));
        ev.Pnp.Add(new(400, DateTime.Now, new object?[] { $@"SWD\MMDEVAPI\{Serial}" }));
        var rec = Assert.Single(Make(Reg(Serial + "&0"), ev, new FakeFiles()).Scan());
        Assert.Null(rec.LastConnected);
    }

    [Fact]
    public void Serial_has_all_trailing_instance_suffixes_stripped()
    {
        var rec = Assert.Single(Make(Reg(Serial + "&1&0"), new FakeEvents(), new FakeFiles()).Scan());
        Assert.Equal(Serial, rec.Serial);
    }

    [Fact]
    public void No_usbstor_key_gives_empty_list()
    {
        Assert.Empty(Make(new FakeRegistry(), new FakeEvents(), new FakeFiles()).Scan());
    }

    [Fact]
    public void Dangling_device_install_does_not_steal_next_section()
    {
        var files = new FakeFiles { Lines = new[]
        {
            Install(Serial),                                   // no Section start follows
            @">>>  [Setup Import Driver Package - c:\x.inf]", ">>>  Section start 2026/05/05 05:05:05.005",
        } };
        var rec = Assert.Single(Make(Reg(Serial + "&0"), new FakeEvents(), files).Scan());
        Assert.Null(rec.FirstConnected);
    }

    [Fact]
    public void Empty_serial_never_matches_anything()
    {
        var ev = new FakeEvents();
        ev.Partition.Add(new(1006, DateTime.Now, new object?[] { "" }));
        ev.Pnp.Add(new(400, DateTime.Now, new object?[] { $@"USBSTOR\{Model}\&0" }));
        var files = new FakeFiles { Lines = new[] { Install(Serial), ">>>  Section start 2026/01/01 01:01:01.001" } };
        var rec = Assert.Single(Make(Reg("&0"), ev, files).Scan());
        Assert.Equal("", rec.Serial);
        Assert.Null(rec.FirstConnected);
        Assert.Null(rec.LastConnected);
    }
}
