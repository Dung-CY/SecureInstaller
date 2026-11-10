using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Scanning;

public enum NetworkKind { Wireless, Wired, Vpn, Other }

public record NetworkProfile(string Name, NetworkKind Kind, DateTime? FirstConnected, DateTime? LastConnected);

public record NetworkHistory(List<NetworkProfile> Profiles, bool InternetNow);

public sealed class NetworkHistoryScanner
{
    private const string ProfilesPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles";
    private readonly IRegistryReader _reg;
    private readonly IConnectivityProbe _probe;

    public NetworkHistoryScanner(IRegistryReader reg, IConnectivityProbe probe)
    {
        _reg = reg;
        _probe = probe;
    }

    public async Task<NetworkHistory> ScanAsync(CancellationToken ct)
    {
        var profiles = new List<NetworkProfile>();
        foreach (var guid in _reg.GetSubKeyNames(RegistryHive.LocalMachine, RegistryView.Registry64, ProfilesPath))
        {
            var path = ProfilesPath + "\\" + guid;
            object? Get(string n) => _reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, path, n);
            if (Get("ProfileName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
            profiles.Add(new NetworkProfile(name, ToKind(Get("NameType")),
                ParseSystemTime(Get("DateCreated")), ParseSystemTime(Get("DateLastConnected"))));
        }

        profiles = profiles
            .OrderBy(p => p.LastConnected.HasValue ? 0 : 1)
            .ThenByDescending(p => p.LastConnected ?? DateTime.MinValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var internet = await _probe.HasInternetAsync(ct).ConfigureAwait(false);
        return new NetworkHistory(profiles, internet);
    }

    private static NetworkKind ToKind(object? v)
    {
        long? n = v switch
        {
            int i => i,
            long l => l,
            uint u => u,
            string s when long.TryParse(s, out var p) => p,
            _ => null
        };
        return n switch
        {
            71 => NetworkKind.Wireless,
            6 => NetworkKind.Wired,
            23 => NetworkKind.Vpn,
            _ => NetworkKind.Other
        };
    }

    public static DateTime? ParseSystemTime(object? value)
    {
        if (value is not byte[] b || b.Length != 16) return null;
        var f = new int[8];
        for (int i = 0; i < 8; i++) f[i] = BitConverter.ToUInt16(b, i * 2);
        if (f[0] == 0 || f[1] is < 1 or > 12 || f[3] is < 1 or > 31 || f[4] > 23 || f[5] > 59 || f[6] > 59 || f[7] > 999)
            return null;
        try
        {
            return new DateTime(f[0], f[1], f[3], f[4], f[5], f[6], f[7], DateTimeKind.Local);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
