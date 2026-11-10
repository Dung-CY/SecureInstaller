using System.Security;
using Microsoft.Win32;

namespace Installer.Core.Platform;

public sealed class RegistryReader : IRegistryReader
{
    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public object? GetValue(RegistryHive hive, RegistryView view, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
