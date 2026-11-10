using Installer.Core.Licensing;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.Core.Rules;

public record RuleResult(bool Allowed, List<string> Reasons, bool NeedsDowngradeConfirm);

public static class RuleEvaluator
{
    public static RuleResult Evaluate(ScanReport scan, PackageEntry pkg, VersionCheck version)
    {
        var reasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in pkg.Rules ?? new List<RuleSpec>())
        {
            var type = rule?.Type;
            if (string.IsNullOrWhiteSpace(type) || !seen.Add(type)) continue;

            string? reason = type.ToLowerInvariant() switch
            {
                "block_if_windows_cracked" when scan.Windows.Verdict == LicenseVerdict.CrackSuspected
                    => Strings.ReasonWindowsCracked,
                "block_if_office_cracked" when scan.Office.Verdict == LicenseVerdict.CrackSuspected
                    => Strings.ReasonOfficeCracked,
                "block_if_installed_version_gte" when version.Status is VersionStatus.Equal or VersionStatus.Newer
                    => string.Format(Strings.ReasonInstalledGte, version.InstalledVersion),
                "block_if_keyword_file" when scan.Keywords.MatchedFiles.Count > 0
                    => string.Format(Strings.ReasonKeywordFiles, scan.Keywords.MatchedFiles.Count),
                "require_internet" when !scan.Network.InternetNow
                    => Strings.ReasonRequireInternet,
                _ => null
            };
            if (reason != null) reasons.Add(reason);
        }

        var allowed = reasons.Count == 0;
        return new RuleResult(allowed, reasons, allowed && version.Status == VersionStatus.Newer);
    }
}
