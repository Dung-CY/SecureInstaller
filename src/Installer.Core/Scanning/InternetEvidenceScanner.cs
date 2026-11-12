using System.Net;
using System.Net.Sockets;
using Installer.Core.Platform;

namespace Installer.Core.Scanning;

public record EvidenceItem(string Source, DateTime Time, string Text);
public enum InternetVerdict { Yes, Indirect, NoEvidence }
public record InternetEvidence(InternetVerdict Verdict, DateTime? LastSeen, List<EvidenceItem> Items);
public record InternetScanResult(InternetEvidence Evidence, IReadOnlyDictionary<string, bool> ByProfile);

/// <summary>
/// Reads event logs only (never localized text). Observed layouts on Windows 11:
/// NetworkProfile 10000 = Name, Description, Guid, Type, State, Category; NCSI 4042 = InterfaceGuid, IfLuid, Family,
/// Capability (0 none, 1 local, 2 internet), Reason, PreviousCapability; Time-Service 37 = "host,0x9 (ntp.m|..|a:123->ip:123)".
/// </summary>
public sealed class InternetEvidenceScanner
{
    private const string ProfileLog = "Microsoft-Windows-NetworkProfile/Operational";
    private const string NcsiLog = "Microsoft-Windows-NCSI/Operational";
    private const uint InternetMask = 0x440;   // NLM_CONNECTIVITY IPV4_INTERNET | IPV6_INTERNET
    private readonly IEventLogReader _log;

    public InternetEvidenceScanner(IEventLogReader log) => _log = log;

    public InternetScanResult Scan()
    {
        var strong = new List<EvidenceItem>();
        var byProfile = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in _log.Read(ProfileLog, new[] { 10000 }))
        {
            if (e.Properties.Count < 5 || e.Properties[0] is not string name || string.IsNullOrWhiteSpace(name)) continue;
            if (ToUInt(e.Properties[4]) is not { } state) continue;
            var has = (state & InternetMask) != 0;
            byProfile[name] = (byProfile.TryGetValue(name, out var old) && old) || has;
            if (has) strong.Add(new("NetworkProfile", e.TimeCreated, $"Mạng \"{name}\" có kết nối internet"));
        }

        foreach (var e in _log.Read(NcsiLog, new[] { 4042 }))
            if (e.Properties.Count > 3 && ToUInt(e.Properties[3]) == 2)
                strong.Add(new("NCSI", e.TimeCreated, "Windows ghi nhận kết nối internet"));

        if (strong.Count > 0) return Result(InternetVerdict.Yes, strong, byProfile);

        var indirect = new List<EvidenceItem>();
        foreach (var e in _log.Read("System", new[] { 19, 43, 44 }))
            if (e.Provider is null or "Microsoft-Windows-WindowsUpdateClient")
                indirect.Add(new("WindowsUpdate", e.TimeCreated, "Windows Update đã hoạt động"));
        foreach (var e in _log.Read("System", new[] { 37 }))
            if (e.Provider is null or "Microsoft-Windows-Time-Service"
                && e.Properties.Count > 0 && e.Properties[0] is string s && PublicPeer(s) is { } peer)
                indirect.Add(new("Time-Service", e.TimeCreated, $"Đồng bộ giờ với máy chủ {peer}"));

        return Result(indirect.Count > 0 ? InternetVerdict.Indirect : InternetVerdict.NoEvidence, indirect, byProfile);
    }

    private static InternetScanResult Result(InternetVerdict v, List<EvidenceItem> items, Dictionary<string, bool> map)
    {
        var sorted = items.OrderByDescending(i => i.Time).ToList();
        return new(new InternetEvidence(v, sorted.Count > 0 ? sorted[0].Time : null, sorted.Take(10).ToList()), map);
    }

    private static uint? ToUInt(object? v) => v switch
    {
        uint u => u, int i when i >= 0 => (uint)i, long l when l is >= 0 and <= uint.MaxValue => (uint)l,
        ulong ul when ul <= uint.MaxValue => (uint)ul, _ => null
    };

    /// <summary>Peer host or IP from "host,0x9 (...)"; null if empty, not a host name, or a private/LAN/loopback IP.</summary>
    internal static string? PublicPeer(string text)
    {
        var host = text.Split(',')[0].Trim();
        if (host.Length == 0) return null;
        if (IPAddress.TryParse(host, out var ip)) return IsPrivate(ip) ? null : host;
        return host.Contains('.') ? host : null;   // VMICTimeProvider and other provider names have no dot
    }

    internal static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
    }
}
