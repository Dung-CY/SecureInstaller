using Microsoft.Win32;

namespace Installer.Core.Platform;

public sealed class RegistryWriter : IRegistryWriter
{
    public void SetString(RegistryHive hive, RegistryView view, string path, string name, string value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.CreateSubKey(path, true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteKey(RegistryHive hive, RegistryView view, string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        baseKey.DeleteSubKeyTree(path, false);
    }
}
