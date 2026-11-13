using System.Text.RegularExpressions;

namespace Installer.Core.Versioning;

public enum VersionStatus { NotInstalled, Older, Equal, Newer, Incomparable }

public record VersionCheck(VersionStatus Status, string? InstalledVersion, string PackageVersion, IReadOnlyList<InstalledApp> Matches);

public static class VersionComparer
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    private static List<string> Parts(string s) =>
        Digits.Matches(s).Select(m => { var t = m.Value.TrimStart('0'); return t.Length == 0 ? "0" : t; }).ToList();

    private static int CompareNum(string a, string b) =>
        a.Length != b.Length ? (a.Length > b.Length ? 1 : -1) : Math.Sign(string.CompareOrdinal(a, b));

    /// <summary>-1/0/1, or null when either side has no digit groups.</summary>
    public static int? Compare(string a, string b)
    {
        var pa = Parts(a); var pb = Parts(b);
        if (pa.Count == 0 || pb.Count == 0) return null;
        var n = Math.Max(pa.Count, pb.Count);
        for (var i = 0; i < n; i++)
        {
            var c = CompareNum(i < pa.Count ? pa[i] : "0", i < pb.Count ? pb[i] : "0");
            if (c != 0) return c;
        }
        return 0;
    }

    /// <summary>
    /// Installers often add a build number Windows shows but the package version omits (8.0.34 vs 8.0.34.36622):
    /// that is the same release. Only applies when the package names at least major.minor.patch.
    /// </summary>
    private static bool IsBuildSuffix(string installed, string package)
    {
        var pi = Parts(installed); var pp = Parts(package);
        return pp.Count >= 3 && pi.Count > pp.Count && pp.Select((x, i) => CompareNum(x, pi[i]) == 0).All(b => b);
    }

    public static VersionCheck Check(IReadOnlyList<InstalledApp> installed, string packageVersion)
    {
        if (installed.Count == 0)
            return new VersionCheck(VersionStatus.NotInstalled, null, packageVersion, installed);

        InstalledApp? best = null;
        foreach (var app in installed)
        {
            if (app.DisplayVersion is null || Compare(app.DisplayVersion, packageVersion) is null) continue;
            if (best is null || Compare(app.DisplayVersion, best.DisplayVersion!) > 0) best = app;
        }
        if (best is null)
            return new VersionCheck(VersionStatus.Incomparable, installed[0].DisplayVersion, packageVersion, installed);

        var c = IsBuildSuffix(best.DisplayVersion!, packageVersion) ? 0 : Compare(best.DisplayVersion!, packageVersion)!.Value;
        var status = c < 0 ? VersionStatus.Older : c == 0 ? VersionStatus.Equal : VersionStatus.Newer;
        return new VersionCheck(status, best.DisplayVersion, packageVersion, installed);
    }
}
