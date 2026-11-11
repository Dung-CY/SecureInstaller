using Installer.Core.Platform;

namespace Installer.Core.Install;

/// <summary>ACL-restricted temp folder holding decrypted installers; removed on Dispose.</summary>
public sealed class TempWorkspace : IDisposable
{
    public string Path { get; }
    public bool Deleted { get; private set; }

    private TempWorkspace(string path) => Path = path;

    public static string DefaultBase => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureInstaller", "work");

    public static TempWorkspace Create(string? baseDir = null, Func<bool>? isElevated = null)
    {
        var baseP = baseDir ?? DefaultBase;
        bool elevated = (isElevated ?? SecureDirectory.IsElevated)();
        SecureDirectory.CreateProtected(baseP, elevated);
        var path = System.IO.Path.Combine(baseP, Guid.NewGuid().ToString("N"));
        SecureDirectory.CreateProtected(path, elevated);
        return new TempWorkspace(path);
    }

    public void Dispose()
    {
        if (Deleted) return;
        Deleted = TryDelete(Path, true);
    }

    /// <summary>Deletes a workspace directory with retries; schedules delete-on-reboot when it fails.</summary>
    private static bool TryDelete(string path, bool scheduleOnFailure)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return true;
                if (IsReparsePoint(path))
                {
                    Directory.Delete(path, false); // removes only the link
                    return true;
                }
                ClearAttributes(path);
                Directory.Delete(path, true);
                return true;
            }
            catch { }
            if (attempt < 2) Thread.Sleep(500);
        }
        if (!Directory.Exists(path)) return true;
        if (scheduleOnFailure) ScheduleDeleteOnReboot(path);
        return false;
    }

    private static readonly EnumerationOptions Recurse = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static void ClearAttributes(string path)
    {
        if (IsReparsePoint(path)) return;
        try
        {
            foreach (var e in Directory.EnumerateFileSystemEntries(path, "*", Recurse))
            {
                try { File.SetAttributes(e, FileAttributes.Normal); } catch { }
            }
            File.SetAttributes(path, FileAttributes.Directory);
        }
        catch { }
    }

    private static void ScheduleDeleteOnReboot(string path)
    {
        try
        {
            if (IsReparsePoint(path)) { NativePaths.DeleteOnReboot(path); return; }
            var entries = Directory.EnumerateFileSystemEntries(path, "*", Recurse).ToList();
            foreach (var f in entries.Where(File.Exists)) NativePaths.DeleteOnReboot(f);
            foreach (var d in entries.Where(Directory.Exists).OrderByDescending(x => x.Length))
                NativePaths.DeleteOnReboot(d);
            NativePaths.DeleteOnReboot(path);
        }
        catch { }
    }

    private const System.Text.RegularExpressions.RegexOptions Opts =
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
    private static readonly System.Text.RegularExpressions.Regex LegacyName = new("^SI_[0-9a-f]{32}$", Opts);
    private static readonly System.Text.RegularExpressions.Regex NewName = new("^[0-9a-f]{32}$", Opts);

    /// <summary>Removes stale workspaces (new base and legacy %TEMP%\SI_*) older than the given age. Never throws.</summary>
    public static int SweepStale(TimeSpan olderThan, string? baseDir = null)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        return Sweep(baseDir ?? DefaultBase, "*", n => NewName.IsMatch(n), cutoff)
             + Sweep(System.IO.Path.GetTempPath(), "SI_*", n => n == "SI_docs" || LegacyName.IsMatch(n), cutoff);
    }

    private static int Sweep(string root, string pattern, Func<string, bool> match, DateTime cutoff)
    {
        int count = 0;
        try
        {
            if (IsReparsePoint(root)) return 0;
            foreach (var d in Directory.EnumerateDirectories(root, pattern))
            {
                try
                {
                    if (!match(System.IO.Path.GetFileName(d))) continue;
                    if (Directory.GetLastWriteTimeUtc(d) < cutoff && TryDelete(d, false)) count++;
                }
                catch { }
            }
        }
        catch { }
        return count;
    }
}
