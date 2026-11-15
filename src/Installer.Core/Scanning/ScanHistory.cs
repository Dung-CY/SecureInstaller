using System.Text.Json;
using Installer.Core.Logging;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public record ScanHistoryItem(DateTimeOffset Ts, string Username, string Hostname, string? BiosSerial, ScanReport Report);

public static class ScanHistory
{
    /// <summary>Cards on the PCI or USB bus; VPN, Hyper-V and other software adapters are left out.
    /// Reports written before the bus was recorded keep every adapter.</summary>
    public static IEnumerable<string> PhysicalMacs(HardwareInfo hw) => (hw.Nics ?? new())
        .Where(n => n.PnpDeviceId is null || n.PnpDeviceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
                    || n.PnpDeviceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
        .Select(n => n.Mac).Distinct(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> DiskSerials(HardwareInfo hw) =>
        (hw.Disks ?? new()).Select(d => d.Serial?.Trim()).OfType<string>().Where(s => s.Length > 0);

    /// <summary>"Đơn vị | Hãng máy | Số seri máy | MAC card vật lý | Seri ổ cứng;…"</summary>
    public static string Line(ScanReport r) => string.Join(" | ", new[]
    {
        r.Unit ?? "", r.Hardware.Manufacturer ?? "", r.Hardware.BiosSerial ?? "",
        string.Join(";", PhysicalMacs(r.Hardware)), string.Join(";", DiskSerials(r.Hardware)),
    });

    public static List<ScanHistoryItem> From(IEnumerable<LogEntry> entries)
    {
        var list = new List<ScanHistoryItem>();
        foreach (var e in entries)
        {
            // Any failure is confined to its own entry so one bad log line cannot break the whole history.
            try
            {
                if (e.Action != "scan" || string.IsNullOrEmpty(e.ScanReportJson)) continue;
                var r = JsonSerializer.Deserialize<ScanReport>(e.ScanReportJson, Json.Options);
                if (r?.Hardware == null) continue;
                list.Add(new ScanHistoryItem(e.Ts, e.Username, e.Hostname, e.BiosSerial, r));
            }
            catch (Exception) { }
        }
        return list.OrderByDescending(i => i.Ts).ToList();
    }
}
