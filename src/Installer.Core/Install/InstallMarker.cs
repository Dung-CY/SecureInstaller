using System.Globalization;
using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Install;

/// <summary>HKLM marker recording packages installed by this app (zip packages have no Uninstall entry).</summary>
public sealed class InstallMarker(IRegistryReader reg, IRegistryWriter writer)
{
    private const string Root = @"SOFTWARE\SecureInstaller\Installed";
    private const RegistryHive Hive = RegistryHive.LocalMachine;
    private const RegistryView View = RegistryView.Registry64;

    public void Write(string packageId, string version, DateTimeOffset at)
    {
        var path = Root + "\\" + packageId;
        writer.SetString(Hive, View, path, "Version", version);
        writer.SetString(Hive, View, path, "InstalledAt", at.ToString("o", CultureInfo.InvariantCulture));
    }

    public void Remove(string packageId) => writer.DeleteKey(Hive, View, Root + "\\" + packageId);

    public IReadOnlyDictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in reg.GetSubKeyNames(Hive, View, Root))
            if (reg.GetValue(Hive, View, Root + "\\" + id, "Version") is string v)
                result[id] = v;
        return result;
    }
}
