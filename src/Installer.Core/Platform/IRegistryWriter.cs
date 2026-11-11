using Microsoft.Win32;

namespace Installer.Core.Platform;

public interface IRegistryWriter
{
    void SetString(RegistryHive hive, RegistryView view, string path, string name, string value);
    void DeleteKey(RegistryHive hive, RegistryView view, string path);
}
