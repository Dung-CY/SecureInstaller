using System.Text.Json;
using Installer.Core.Logging;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public record ScanHistoryItem(DateTimeOffset Ts, string Username, string Hostname, string? BiosSerial, ScanReport Report);

public static class ScanHistory
{
    public static List<ScanHistoryItem> From(IEnumerable<LogEntry> entries)
    {
        var list = new List<ScanHistoryItem>();
        foreach (var e in entries)
        {
            if (e.Action != "scan" || string.IsNullOrEmpty(e.ScanReportJson)) continue;
            try
            {
                var r = JsonSerializer.Deserialize<ScanReport>(e.ScanReportJson, Json.Options);
                if (r?.Hardware == null) continue;
                list.Add(new ScanHistoryItem(e.Ts, e.Username, e.Hostname, e.BiosSerial, r));
            }
            catch (JsonException) { }
        }
        return list.OrderByDescending(i => i.Ts).ToList();
    }
}
