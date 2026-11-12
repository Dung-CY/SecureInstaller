using System.Diagnostics;
using System.Text;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public record KeywordScanResult(List<string> MatchedFiles, bool TimedOut, bool Truncated);

public sealed class KeywordFileScanner
{
    public const int MaxMatches = 1000;
    public const string DesktopToken = "{desktop}", DownloadsToken = "{downloads}", DataDrivesToken = "{data-drives}";

    private readonly string _usersRoot;
    private readonly Func<IEnumerable<string>> _dataDrives;

    /// <param name="usersRoot">Folder holding the profiles; default is the Users folder of the system drive.</param>
    /// <param name="dataDrives">Roots to scan for {data-drives}; default is every ready fixed drive except the Windows one.</param>
    public KeywordFileScanner(string? usersRoot = null, Func<IEnumerable<string>>? dataDrives = null)
    {
        _usersRoot = usersRoot ?? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users");
        _dataDrives = dataDrives ?? DefaultDataDrives;
    }

    private static IEnumerable<string> DefaultDataDrives()
    {
        var win = Path.GetPathRoot(Environment.SystemDirectory);
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady
                && !string.Equals(d.RootDirectory.FullName, win, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.RootDirectory.FullName).ToList();
    }

    private static readonly string[] SpecialProfiles = { "Default", "Default User", "All Users" };

    private IEnumerable<string> Profiles()
    {
        if (!Directory.Exists(_usersRoot)) yield break;
        foreach (var d in new DirectoryInfo(_usersRoot).EnumerateDirectories())
        {
            if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (SpecialProfiles.Contains(d.Name, StringComparer.OrdinalIgnoreCase)) continue;
            yield return d.FullName;
        }
    }

    internal IEnumerable<string> ExpandTokens(string raw)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case DesktopToken:
                var list = new List<string> { Path.Combine(_usersRoot, "Public", "Desktop") };
                foreach (var p in Profiles())
                {
                    list.Add(Path.Combine(p, "Desktop"));
                    try
                    {
                        foreach (var od in new DirectoryInfo(p).EnumerateDirectories("OneDrive*"))
                            if ((od.Attributes & FileAttributes.ReparsePoint) == 0) list.Add(Path.Combine(od.FullName, "Desktop"));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return list;
            case DownloadsToken: return Profiles().Select(p => Path.Combine(p, "Downloads")).ToList();
            case DataDrivesToken: return _dataDrives();
            default: return new[] { raw };
        }
    }

    /// <remarks>
    /// The deadline and cancellation are checked for every enumerated file. A single blocked
    /// directory read (for example on a dead network share) cannot be interrupted.
    /// </remarks>
    public KeywordScanResult Scan(KeywordScanConfig cfg, CancellationToken ct)
    {
        var keywords = (cfg.Keywords ?? new())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Normalize(NormalizationForm.FormC))
            .ToList();
        var matches = new List<string>();
        if (keywords.Count == 0) return new(matches, false, false);
        if (cfg.MaxSeconds <= 0) return new(matches, true, false);

        var sw = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(cfg.MaxSeconds);
        bool Expired() => ct.IsCancellationRequested || sw.Elapsed >= limit;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var paths = new List<string>();
        foreach (var r in cfg.Paths ?? new())
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            try { paths.AddRange(ExpandTokens(r)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        foreach (var raw in paths)
        {
            if (Expired()) return new(matches, true, false);
            try
            {
                var root = Environment.ExpandEnvironmentVariables(raw);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", opts))
                {
                    if (Expired()) return new(matches, true, false);
                    if (!NameMatches(Path.GetFileName(file), keywords)) continue;
                    if (!seen.Add(Path.GetFullPath(file))) continue;
                    if (matches.Count >= MaxMatches) return new(matches, false, true);
                    matches.Add(file);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return new(matches, false, false);
    }

    /// <summary>Keywords must already be NFC-normalized and non-blank.</summary>
    internal static bool NameMatches(string fileName, IReadOnlyList<string> keywords)
    {
        var name = fileName.Normalize(NormalizationForm.FormC);
        foreach (var k in keywords)
            if (name.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
