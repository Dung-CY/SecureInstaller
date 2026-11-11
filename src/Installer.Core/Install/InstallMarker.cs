using System.Globalization;
using System.Text.RegularExpressions;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Install;

/// <summary>HKLM marker recording packages installed by this app (zip packages have no Uninstall entry).</summary>
public sealed class InstallMarker(IRegistryReader reg, IRegistryWriter writer)
{
    private const string Root = @"SOFTWARE\SecureInstaller\Installed";
    private const RegistryHive Hive = RegistryHive.LocalMachine;
    private const RegistryView View = RegistryView.Registry64;

    private static readonly Regex IdRx = new("^[A-Za-z0-9._-]+\\z", RegexOptions.Compiled);

    private static bool IsValid(string id) => id != null && IdRx.IsMatch(id) && id != "." && id != "..";

    private static void Check(string id)
    {
        if (!IsValid(id)) throw new ArgumentException("Invalid package id for install marker.", nameof(id));
    }

    public void Write(string packageId, string version, DateTimeOffset at)
    {
        Check(packageId);
        var path = Root + "\\" + packageId;
        writer.SetString(Hive, View, path, "Version", version);
        writer.SetString(Hive, View, path, "InstalledAt", at.ToString("o", CultureInfo.InvariantCulture));
    }

    public void Remove(string packageId)
    {
        Check(packageId);
        writer.DeleteKey(Hive, View, Root + "\\" + packageId);
    }

    public IReadOnlyDictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in reg.GetSubKeyNames(Hive, View, Root))
            if (IsValid(id))
            if (reg.GetValue(Hive, View, Root + "\\" + id, "Version") is string v)
                result[id] = v;
        return result;
    }
}
