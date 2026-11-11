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
        var ws = new TempWorkspace(path);
        // Fail closed: report the problem but never delete anything on a verification failure.
        if (!SecureDirectory.VerifyChain(path, elevated))
            throw new Errors.WorkspaceUnsafeException(Strings.WorkspaceUnsafe);
        return ws;
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

    private static readonly System.Text.RegularExpressions.Regex NewName =
        new("^[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Removes stale workspaces in the base older than the given age, only if the base chain verifies. Never throws.</summary>
    public static int SweepStale(TimeSpan olderThan, string? baseDir = null, Func<string, bool>? verify = null, Func<bool>? isElevated = null)
    {
        try
        {
            var root = baseDir ?? DefaultBase;
            bool elevated = (isElevated ?? SecureDirectory.IsElevated)();
            if (!(verify ?? (p => SecureDirectory.VerifyChain(p, elevated)))(root)) return 0;
            return Sweep(root, DateTime.UtcNow - olderThan);
        }
        catch { return 0; }
    }

    private static int Sweep(string root, DateTime cutoff)
    {
        int count = 0;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (!NewName.IsMatch(System.IO.Path.GetFileName(d))) continue;
                    if (IsReparsePoint(d)) continue;
                    if (Directory.GetLastWriteTimeUtc(d) < cutoff && TryDelete(d, false)) count++;
                }
                catch { }
            }
        }
        catch { }
        return count;
    }
}
