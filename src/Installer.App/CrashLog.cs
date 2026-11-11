using System.IO;

namespace Installer.App;

public static class CrashLog
{
    private const long MaxBytes = 1024 * 1024;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureInstaller", "crash.log");

    /// <summary>Best effort; never throws.</summary>
    public static void Append(Exception ex, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, "crash.old.log"), true);
            }
            catch { }
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
