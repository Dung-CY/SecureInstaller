using System.Text.RegularExpressions;
using Installer.Core.Model;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Versioning;

public record InstalledApp(string DisplayName, string? DisplayVersion, string KeyName, bool Is64Bit, bool PerUser);

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
    public InstalledAppsScanner(IRegistryReader reg) => _reg = reg;

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
                    if (!byCode && !byName) continue;

                    var raw = _reg.GetValue(hive, view, path, "DisplayVersion");
                    var ver = raw is string s ? s : raw?.ToString();
                    if (string.IsNullOrEmpty(ver)) ver = null;
                    result.Add(new InstalledApp(name, ver, sub, is64, perUser));
                }
                catch (Exception) { /* a bad entry must never break the scan */ }
            }
        }
        return result;
    }
}
