using Installer.Core.Platform;

namespace Installer.Core.Scanning;

public record DiskInfo(string Model, string? Serial, string? InterfaceType);
public record NicInfo(string Name, string Mac);
public record DeviceInfo(string Name, string? PnpClass, string DeviceId);
public record HardwareInfo(string Hostname, string? Manufacturer, string? Model, string? BiosSerial,
    List<DiskInfo> Disks, List<NicInfo> Nics, List<DeviceInfo> Peripherals, List<string> Errors);

public sealed class HardwareScanner
{
    private static readonly string[] PeripheralPrefixes = { @"USB\", @"USBSTOR\", @"HID\", @"BTHENUM\", @"SWD\PRINTENUM\" };
    private static readonly HashSet<string> PeripheralClasses =
        new(new[] { "Printer", "Camera", "Image", "Bluetooth", "Keyboard", "Mouse", "MEDIA" }, StringComparer.OrdinalIgnoreCase);

    private readonly IWmi _wmi;
    private readonly ISystemInfo _sys;

    public HardwareScanner(IWmi wmi, ISystemInfo sys) { _wmi = wmi; _sys = sys; }

    public HardwareInfo Scan()
    {
        var errors = new List<string>();
        string? manufacturer = null, model = null, biosSerial = null;
        var disks = new List<DiskInfo>();
        var nics = new List<NicInfo>();
        var peripherals = new List<DeviceInfo>();

        var cs = Run("Win32_ComputerSystem", "SELECT Manufacturer, Model FROM Win32_ComputerSystem", errors);
        if (cs.Count > 0) { manufacturer = Str(cs[0], "Manufacturer"); model = Str(cs[0], "Model"); }

        var bios = Run("Win32_BIOS", "SELECT SerialNumber FROM Win32_BIOS", errors);
        if (bios.Count > 0) biosSerial = Str(bios[0], "SerialNumber");

        foreach (var r in Run("Win32_DiskDrive", "SELECT Model, SerialNumber, InterfaceType FROM Win32_DiskDrive", errors))
            disks.Add(new DiskInfo(Str(r, "Model") ?? "", Str(r, "SerialNumber"), Str(r, "InterfaceType")));

        foreach (var r in Run("Win32_NetworkAdapter",
                     "SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL", errors))
        {
            var mac = Str(r, "MACAddress");
            if (mac == null) continue;
            nics.Add(new NicInfo(Str(r, "Name") ?? "", mac));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Run("Win32_PnPEntity", "SELECT Name, PNPClass, PNPDeviceID FROM Win32_PnPEntity WHERE Present = TRUE", errors))
        {
            var id = Str(r, "PNPDeviceID");
            if (id == null) continue;
            var cls = Str(r, "PNPClass");
            var match = PeripheralPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        || (cls != null && PeripheralClasses.Contains(cls));
            if (!match || !seen.Add(id)) continue;
            peripherals.Add(new DeviceInfo(Str(r, "Name") ?? "", cls, id));
        }

        return new HardwareInfo(_sys.Hostname, manufacturer, model, biosSerial, disks, nics, peripherals, errors);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Run(string cls, string wql, List<string> errors)
    {
        try { return _wmi.Query(wql); }
        catch (Exception e)
        {
            errors.Add($"{cls}: {e.Message}");
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }
    }

    private static string? Str(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v)) return null;
        var s = v?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
