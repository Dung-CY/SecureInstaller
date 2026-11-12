using System.Text.RegularExpressions;
using Installer.Core.Install;
using Installer.Core.Model;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Versioning;

public record InstalledApp(string DisplayName, string? DisplayVersion, string KeyName, bool Is64Bit, bool PerUser,
    string? UninstallString = null, string? QuietUninstallString = null, string? DisplayIcon = null, bool IsMarker = false, bool SystemComponent = false);

public sealed class InstalledAppsScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View, bool Is64Bit, bool PerUser)[] Sources =
    {
        (RegistryHive.LocalMachine, RegistryView.Registry64, true, false),
        (RegistryHive.LocalMachine, RegistryView.Registry32, false, false),
        (RegistryHive.CurrentUser, RegistryView.Registry64, true, true),
    };

    private readonly IRegistryReader _reg;
    private readonly InstallMarker? _marker;
    public InstalledAppsScanner(IRegistryReader reg, InstallMarker? marker = null) { _reg = reg; _marker = marker; }

    /// <summary>
    /// Returns Find(pkg.Detect). The install marker is only a fallback: it is used solely when the
    /// detect rule matches nothing (e.g. zip packages), so a stale marker never overrides or duplicates real entries.
    /// </summary>
    public IReadOnlyList<InstalledApp> FindForPackage(PackageEntry pkg)
    {
        var result = new List<InstalledApp>();
        try
        {
            // Markers written before hidden keys were skipped may point at a bundle's hidden child: ignore those.
            if (_marker?.ReadUninstallKey(pkg.Id) is { } k && FindByKey(k) is { SystemComponent: false } byKey) result.Add(byKey);
        }
        catch (Exception) { }
        foreach (var a in Find(pkg.Detect))
            if (!result.Any(r => r.KeyName == a.KeyName && r.Is64Bit == a.Is64Bit)) result.Add(a);
        if (result.Count > 0) return result;
        try { if (FallbackRow(pkg) is { } fb) { result.Add(fb); return result; } }
        catch (Exception) { }
        try
        {
            if (_marker != null && _marker.ReadAll().TryGetValue(pkg.Id, out var v))
                result.Add(new InstalledApp(pkg.Name, v, "marker:" + pkg.Id, true, false, IsMarker: true));
        }
        catch (Exception) { }
        return result;
    }

    private static readonly Regex Word = new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

    /// <summary>Last resort when neither the marker nor Detect finds the app: a visible HKLM row with the package version and a shared word (3+ letters) in the name.</summary>
    private InstalledApp? FallbackRow(PackageEntry pkg)
    {
        var words = Word.Matches(pkg.Name).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (words.Count == 0) return null;
        foreach (var k in SnapshotMachineKeys())
            if (FindByKey(k) is { SystemComponent: false, DisplayVersion: { } v } a
                && VersionComparer.Compare(v, pkg.Version) == 0
                && Word.Matches(a.DisplayName).Any(m => words.Contains(m.Value)))
                return a;
        return null;
    }

    private static string KeyOf(bool is64, string sub) => (is64 ? "64|" : "32|") + sub;

    /// <summary>HKLM Uninstall subkeys of both views as "64|name" / "32|name" (never HKCU).</summary>
    public HashSet<string> SnapshotMachineKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, is64, perUser) in Sources)
            if (!perUser)
                foreach (var sub in _reg.GetSubKeyNames(hive, view, UninstallPath)) set.Add(KeyOf(is64, sub));
        return set;
    }

    /// <summary>The HKLM row for a "64|name" / "32|name" key, or null when it is malformed or gone.</summary>
    public InstalledApp? FindByKey(string key)
    {
        var bar = key.IndexOf('|');
        if (bar < 0 || key[..bar] is not ("64" or "32")) return null;
        var sub = key[(bar + 1)..];
        if (sub.Length == 0 || sub.Contains('\\')) return null;
        var is64 = key[..bar] == "64";
        var view = is64 ? RegistryView.Registry64 : RegistryView.Registry32;
        var path = UninstallPath + "\\" + sub;
        if (_reg.GetValue(RegistryHive.LocalMachine, view, path, "DisplayName") is not string name || name.Length == 0) return null;
        var raw = _reg.GetValue(RegistryHive.LocalMachine, view, path, "DisplayVersion");
        var ver = raw is string s ? s : raw?.ToString();
        return new InstalledApp(name, string.IsNullOrEmpty(ver) ? null : ver, sub, is64, false,
            Str(RegistryHive.LocalMachine, view, path, "UninstallString"),
            Str(RegistryHive.LocalMachine, view, path, "QuietUninstallString"),
            Str(RegistryHive.LocalMachine, view, path, "DisplayIcon"),
            SystemComponent: _reg.GetValue(RegistryHive.LocalMachine, view, path, "SystemComponent") is 1);
    }

    public bool KeyExists(InstalledApp app) => !app.PerUser && !app.IsMarker && FindByKey(KeyOf(app.Is64Bit, app.KeyName)) != null;

    /// <summary>Picks the Uninstall key an install created: after minus <paramref name="before"/>, else the Find(Detect) HKLM row.</summary>
    public string? PickNewKey(IReadOnlySet<string> before, DetectRule detect, PackageEntry? pkg = null)
    {
        // Hidden (SystemComponent=1) keys are child components of a bundle, never the entry to uninstall.
        var added = SnapshotMachineKeys().Where(k => !before.Contains(k) && FindByKey(k) is not { SystemComponent: true }).ToList();
        if (added.Count == 1) return added[0];
        if (added.Count > 1)
        {
            var rows = added.Select(k => (Key: k, App: FindByKey(k))).Where(x => x.App != null).ToList();
            Regex? rx = null;
            try { if (detect.DisplayNameRegex is not null) rx = new Regex(detect.DisplayNameRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { }
            if (rx != null)
                foreach (var r in rows)
                    try { if (rx.IsMatch(r.App!.DisplayName)) return r.Key; } catch (RegexMatchTimeoutException) { }
            return rows.FirstOrDefault(r => r.App!.UninstallString != null).Key;
        }
        var found = Find(detect).FirstOrDefault(a => !a.PerUser) ?? (pkg == null ? null : FallbackRow(pkg));
        return found == null ? null : KeyOf(found.Is64Bit, found.KeyName);
    }

    private string? Str(RegistryHive hive, RegistryView view, string path, string name) =>
        _reg.GetValue(hive, view, path, name) is string s && s.Length > 0 ? s : null;

    public IReadOnlyList<InstalledApp> Find(DetectRule rule)
    {
        var result = new List<InstalledApp>();
        if (rule.ProductCode is null && rule.DisplayNameRegex is null) return result;

        Regex? rx = null;
        if (rule.DisplayNameRegex is not null)
        {
            try { rx = new Regex(rule.DisplayNameRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { }
        }

        foreach (var (hive, view, is64, perUser) in Sources)
        {
            foreach (var sub in _reg.GetSubKeyNames(hive, view, UninstallPath))
            {
                try
                {
                    var path = UninstallPath + "\\" + sub;
                    if (_reg.GetValue(hive, view, path, "DisplayName") is not string name || name.Length == 0) continue;

                    var byCode = rule.ProductCode is not null &&
                                 string.Equals(sub, rule.ProductCode, StringComparison.OrdinalIgnoreCase);
                    var byName = false;
                    if (!byCode && rx is not null)
                    {
                        try { byName = rx.IsMatch(name); } catch (RegexMatchTimeoutException) { }
                    }
                    var hidden = _reg.GetValue(hive, view, path, "SystemComponent") is 1;
                    if (!byCode && (!byName || hidden)) continue;

                    var raw = _reg.GetValue(hive, view, path, "DisplayVersion");
                    var ver = raw is string s ? s : raw?.ToString();
                    if (string.IsNullOrEmpty(ver)) ver = null;
                    result.Add(new InstalledApp(name, ver, sub, is64, perUser,
                        Str(hive, view, path, "UninstallString"), Str(hive, view, path, "QuietUninstallString"),
                        Str(hive, view, path, "DisplayIcon"), SystemComponent: hidden));
                }
                catch (Exception) { /* a bad entry must never break the scan */ }
            }
        }
        return result;
    }
}
