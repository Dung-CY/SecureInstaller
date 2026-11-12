using System.Globalization;
using System.Text.RegularExpressions;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Scanning;

public record UsbStorageRecord(string FriendlyName, string Serial, string DeviceId, DateTime? FirstConnected, DateTime? LastConnected);

public sealed class UsbStorageHistoryScanner
{
    private const string UsbStorPath = @"SYSTEM\CurrentControlSet\Enum\USBSTOR";
    private const string PartitionLog = "Microsoft-Windows-Partition/Diagnostic";
    private const string PnpLog = "Microsoft-Windows-Kernel-PnP/Configuration";
    private const string InstallMarker = ">>>  [Device Install";
    private const string SectionMarker = ">>>  Section start ";

    private readonly IRegistryReader _reg;
    private readonly IEventLogReader _events;
    private readonly IFileTextReader _files;
    private readonly string _infDir;

    public UsbStorageHistoryScanner(IRegistryReader reg, IEventLogReader events, IFileTextReader files, string? infDir = null)
    {
        _reg = reg; _events = events; _files = files;
        _infDir = infDir ?? Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "INF");
    }

    public List<UsbStorageRecord> Scan()
    {
        var devices = new List<(string Name, string Serial, string Id)>();
        foreach (var model in _reg.GetSubKeyNames(RegistryHive.LocalMachine, RegistryView.Registry64, UsbStorPath))
        {
            foreach (var inst in _reg.GetSubKeyNames(RegistryHive.LocalMachine, RegistryView.Registry64, UsbStorPath + "\\" + model))
            {
                var friendly = _reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64,
                    UsbStorPath + "\\" + model + "\\" + inst, "FriendlyName") as string;
                var serial = Regex.Replace(inst, @"(&\d+)+$", "");
                devices.Add((string.IsNullOrWhiteSpace(friendly) ? model : friendly.Trim(), serial, $@"USBSTOR\{model}\{inst}"));
            }
        }
        if (devices.Count == 0) return new();

        var patterns = devices.Select(d => d.Serial.Length == 0 ? null : new Regex(@"[#\\]" + Regex.Escape(d.Serial) + @"(&\d+)*[#\\\]]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();
        var first = FirstTimes(patterns);

        var ev = new List<EventRecordInfo>();
        ev.AddRange(_events.Read(PartitionLog, new[] { 1006 }));
        ev.AddRange(_events.Read(PnpLog, new[] { 400, 410 }));

        var result = new List<UsbStorageRecord>();
        for (int i = 0; i < devices.Count; i++)
        {
            var (name, serial, id) = devices[i];
            DateTime? last = null;
            foreach (var e in ev)
            {
                if (patterns[i] is not { } rx || !Matches(e, serial, rx)) continue;   // empty serial: never matches
                var t = e.TimeCreated;   // IEventLogReader contract: already local
                if (last is null || t > last) last = t;
            }
            result.Add(new UsbStorageRecord(name, serial, id, first[i], last));
        }
        return result;
    }

    // Partition 1006 carries the serial as a plain string property (index 17 on this machine, but any
    // position is accepted); Kernel-PnP 400/410 carries the device instance path in Properties[0].
    private static bool Matches(EventRecordInfo e, string serial, Regex path)
    {
        if (e.Properties is null) return false;
        if (e.Id == 1006)
            return e.Properties.Any(p => p is string s && s.Trim().Equals(serial, StringComparison.OrdinalIgnoreCase));
        return e.Properties.Count > 0 && e.Properties[0] is string inst
            && inst.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) && path.IsMatch(inst + "\\");
    }

    private DateTime?[] FirstTimes(List<Regex?> patterns)
    {
        var first = new DateTime?[patterns.Count];
        var pending = new List<int>();
        foreach (var line in _files.ReadLines(_infDir, "setupapi.dev*.log"))
        {
            if (line.StartsWith(">>>  [", StringComparison.Ordinal))   // any new section header ends a dangling install
            {
                pending.Clear();
                if (line.StartsWith(InstallMarker, StringComparison.Ordinal) && line.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase))
                    for (int i = 0; i < patterns.Count; i++) if (patterns[i]?.IsMatch(line) == true) pending.Add(i);
            }
            else if (pending.Count > 0 && line.StartsWith(SectionMarker, StringComparison.Ordinal))
            {
                if (DateTime.TryParseExact(line[SectionMarker.Length..].Trim(), "yyyy/MM/dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    foreach (var i in pending) if (first[i] is null || t < first[i]) first[i] = t;
                    pending.Clear();
                }
            }
        }
        return first;
    }
}
