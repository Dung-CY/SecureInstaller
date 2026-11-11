using Microsoft.Win32;

namespace Installer.Core.Platform;

public interface IRegistryReader
{
    /// <summary>Key does not exist (or is inaccessible): empty list.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path);

    /// <summary>Key or value does not exist (or is inaccessible): null.</summary>
    object? GetValue(RegistryHive hive, RegistryView view, string path, string name);
}
