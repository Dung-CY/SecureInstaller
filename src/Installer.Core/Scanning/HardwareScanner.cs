using Installer.Core.Platform;

namespace Installer.Core.Scanning;

public record DiskInfo(string Model, string? Serial, string? InterfaceType);
public record NicInfo(string Name, string Mac, string? PnpDeviceId = null);
public record DeviceInfo(string Name, string? PnpClass, string DeviceId, DateTime? FirstConnected = null, DateTime? LastConnected = null);
public record HardwareInfo(string Hostname, string? Manufacturer, string? Model, string? BiosSerial,
    List<DiskInfo> Disks, List<NicInfo> Nics, List<DeviceInfo> Peripherals, List<string> Errors);

public sealed class HardwareScanner
{
    private static readonly string[] PeripheralPrefixes = { @"USB\", @"USBSTOR\", @"HID\", @"BTHENUM\", @"SWD\PRINTENUM\" };
    private static readonly HashSet<string> PeripheralClasses =
        new(new[] { "Printer", "Camera", "Image", "Bluetooth", "Keyboard", "Mouse", "MEDIA" }, StringComparer.OrdinalIgnoreCase);

    private readonly IWmi _wmi;
    private readonly ISystemInfo _sys;
    private readonly IDeviceDates? _dates;

    public HardwareScanner(IWmi wmi, ISystemInfo sys, IDeviceDates? dates = null) { _wmi = wmi; _sys = sys; _dates = dates; }

    public HardwareInfo Scan()
    {
        var errors = new List<string>();
        string? manufacturer = null, model = null, biosSerial = null;
        var disks = new List<DiskInfo>();
        var nics = new List<NicInfo>();
        var peripherals = new List<DeviceInfo>();

        var cs = Run("Win32_ComputerSystem", "SELECT Manufacturer, Model FROM Win32_ComputerSystem", errors);
        if (cs.Count > 0) { manufacturer = Str(cs[0], "Manufacturer"); model = Str(cs[0], "Model"); }

        biosSerial = MachineSerial(errors);

        foreach (var r in Run("Win32_DiskDrive", "SELECT Model, SerialNumber, InterfaceType FROM Win32_DiskDrive", errors))
            disks.Add(new DiskInfo(Str(r, "Model") ?? "", Str(r, "SerialNumber"), Str(r, "InterfaceType")));

        foreach (var r in Run("Win32_NetworkAdapter",
                     "SELECT Name, MACAddress, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL", errors))
        {
            var mac = Str(r, "MACAddress");
            if (mac == null) continue;
            nics.Add(new NicInfo(Str(r, "Name") ?? "", mac, Str(r, "PNPDeviceID")));
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
            var (first, last) = _dates?.Get(id) ?? (null, null);
            peripherals.Add(new DeviceInfo(Str(r, "Name") ?? "", cls, id, first, last));
        }

        return new HardwareInfo(_sys.Hostname, manufacturer, model, biosSerial, disks, nics, peripherals, errors);
    }

    /// <summary>Cheap WMI queries, used to recognise the machine without a full scan; null when unreadable.</summary>
    public string? BiosSerial() => MachineSerial(new List<string>());

    // Values board makers leave in the BIOS when they never set a serial.
    static readonly HashSet<string> PlaceholderSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default string", "To be filled by O.E.M.", "To Be Filled By O.E.M.", "System Serial Number", "Not Specified",
        "Not Applicable", "None", "OEM", "O.E.M.", "0", "00000000", "Base Board Serial Number", "Chassis Serial Number",
    };

    /// <summary>BIOS serial; when it is a placeholder such as "Default string", the mainboard serial instead (or null).</summary>
    private string? MachineSerial(List<string> errors)
    {
        static string? Real(string? s) => s is null || PlaceholderSerials.Contains(s) ? null : s;
        var bios = Run("Win32_BIOS", "SELECT SerialNumber FROM Win32_BIOS", errors);
        if (bios.Count > 0 && Real(Str(bios[0], "SerialNumber")) is { } b) return b;
        var board = Run("Win32_BaseBoard", "SELECT SerialNumber FROM Win32_BaseBoard", errors);
        return board.Count > 0 ? Real(Str(board[0], "SerialNumber")) : null;
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
