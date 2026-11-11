using System.IO;

namespace Installer.App;

internal static class CrashLog
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureInstaller", "crash.log");

    /// <summary>Best effort; never throws.</summary>
    public static void Append(Exception ex, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
