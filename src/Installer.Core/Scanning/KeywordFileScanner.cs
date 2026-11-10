using System.Diagnostics;
using System.Text;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public record KeywordScanResult(List<string> MatchedFiles, bool TimedOut, bool Truncated);

public sealed class KeywordFileScanner
{
    public const int MaxMatches = 1000;
    private const int CheckEvery = 256;

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
        int counter = 0;

        foreach (var raw in cfg.Paths ?? new())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (Expired()) return new(matches, true, false);
            try
            {
                var root = Environment.ExpandEnvironmentVariables(raw);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", opts))
                {
                    if (++counter % CheckEvery == 0 && Expired()) return new(matches, true, false);
                    if (!NameMatches(Path.GetFileName(file), keywords)) continue;
                    if (!seen.Add(Path.GetFullPath(file))) continue;
                    if (matches.Count >= MaxMatches) return new(matches, false, true);
                    matches.Add(file);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new(matches, false, false);
    }

    internal static bool NameMatches(string fileName, IReadOnlyList<string> keywords)
    {
        var name = fileName.Normalize(NormalizationForm.FormC);
        foreach (var k in keywords)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (name.Contains(k.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
