using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;

namespace Installer.Core.Tests.Scanning;

public class HardwareScannerTests
{
    private static Dictionary<string, object?> R(params (string k, object? v)[] kv) =>
        kv.ToDictionary(x => x.k, x => x.v);

    private static FakeWmi Build()
    {
        var w = new FakeWmi();
        w.Rows["Win32_ComputerSystem"] = new() { R(("Manufacturer", "Dell Inc."), ("Model", "Latitude 5420")) };
        w.Rows["Win32_BIOS"] = new() { R(("SerialNumber", "ABC123")) };
        w.Rows["Win32_DiskDrive"] = new() { R(("Model", "Samsung SSD"), ("SerialNumber", "  S4EVNX0N  "), ("InterfaceType", "SCSI")) };
        w.Rows["Win32_NetworkAdapter"] = new()
        {
            R(("Name", "Intel Ethernet"), ("MACAddress", "AA:BB:CC:DD:EE:FF")),
            R(("Name", "No MAC"), ("MACAddress", null)),
        };
        w.Rows["Win32_PnPEntity"] = new()
        {
            R(("Name", "USB Mouse"), ("PNPClass", "Mouse"), ("PNPDeviceID", @"HID\VID_046D&PID_C077\6&1")),
            R(("Name", "USB Mouse dup"), ("PNPClass", "Mouse"), ("PNPDeviceID", @"hid\vid_046d&pid_c077\6&1")),
            R(("Name", "Flash"), ("PNPClass", "USB"), ("PNPDeviceID", @"USB\VID_0781&PID_5567\1")),
            R(("Name", "Printer"), ("PNPClass", "Printer"), ("PNPDeviceID", @"SWD\PRINTENUM\{1}")),
            R(("Name", "PCI bridge"), ("PNPClass", "System"), ("PNPDeviceID", @"PCI\VEN_8086&DEV_1234")),
        };
        return w;
    }

    [Fact]
    public void Scan_maps_all_fields_from_fake_wmi()
    {
        var info = new HardwareScanner(Build(), new FakeSystemInfo { Hostname = "PC01" }).Scan();
        Assert.Equal("PC01", info.Hostname);
        Assert.Equal("Dell Inc.", info.Manufacturer);
        Assert.Equal("Latitude 5420", info.Model);
        Assert.Equal("ABC123", info.BiosSerial);
        var d = Assert.Single(info.Disks);
        Assert.Equal("Samsung SSD", d.Model);
        Assert.Equal("SCSI", d.InterfaceType);
        var n = Assert.Single(info.Nics);
        Assert.Equal("AA:BB:CC:DD:EE:FF", n.Mac);
        Assert.Empty(info.Errors);
    }

    [Fact]
    public void Disk_serial_is_trimmed()
    {
        var info = new HardwareScanner(Build(), new FakeSystemInfo()).Scan();
        Assert.Equal("S4EVNX0N", info.Disks[0].Serial);
    }

    [Fact]
    public void Peripherals_keep_usb_hid_printer_drop_pci()
    {
        var info = new HardwareScanner(Build(), new FakeSystemInfo()).Scan();
        Assert.Equal(3, info.Peripherals.Count);
        Assert.DoesNotContain(info.Peripherals, p => p.DeviceId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Failing_bios_query_records_error_and_keeps_others()
    {
        var w = Build();
        w.Throws.Add("Win32_BIOS");
        var info = new HardwareScanner(w, new FakeSystemInfo()).Scan();
        Assert.Null(info.BiosSerial);
        Assert.StartsWith("Win32_BIOS", info.Errors[0]);
        Assert.Single(info.Errors);
        Assert.NotEmpty(info.Nics);
    }

    [Fact]
    public void Real_wmi_returns_bios_serial_row()
    {
        var rows = new Installer.Core.Platform.Wmi().Query("SELECT SerialNumber FROM Win32_BIOS");
        Assert.True(rows.Count >= 1);
    }

    sealed class FakeDates : Installer.Core.Platform.IDeviceDates
    {
        public (DateTime? First, DateTime? Last) Get(string id) =>
            id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ? (new DateTime(2026, 1, 2), new DateTime(2026, 11, 15)) : (null, null);
    }

    [Fact]
    public void Peripherals_carry_windows_device_dates_when_known()
    {
        var info = new HardwareScanner(Build(), new FakeSystemInfo { Hostname = "PC01" }, new FakeDates()).Scan();
        var flash = info.Peripherals.Single(p => p.Name == "Flash");
        Assert.Equal((new DateTime(2026, 1, 2), new DateTime(2026, 11, 15)), (flash.FirstConnected, flash.LastConnected));
        Assert.Null(info.Peripherals.Single(p => p.Name == "Printer").FirstConnected);
    }
}
