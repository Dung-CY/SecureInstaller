using Installer.Core;
using Installer.Core.Licensing;
using Installer.Core.Model;
using Installer.Core.Rules;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.Core.Tests.Rules;

public class RuleEvaluatorTests
{
    private static LicenseInfo Lic(LicenseVerdict v) =>
        new("P", null, LicenseChannel.Unknown, null, null, null, v, new List<string>());

    private static ScanReport Report(LicenseVerdict win = LicenseVerdict.Genuine,
        LicenseVerdict office = LicenseVerdict.NotInstalled, bool internet = true, int keywordHits = 0) =>
        new(DateTimeOffset.UtcNow,
            new HardwareInfo("h", null, null, null, new(), new(), new(), new()),
            new NetworkHistory(new(), internet),
            Lic(win), Lic(office),
            new KeywordScanResult(Enumerable.Range(0, keywordHits).Select(i => $"f{i}").ToList(), false, false));

    private static PackageEntry Pkg(params string?[] rules) =>
        new("id", "n", "1.0", InstallerType.Msi, "a.msi", "", null, 60, new DetectRule(null, null),
            new(), new(), rules.Select(r => new RuleSpec(r!)).ToList(), "b", "s", "k");

    private static VersionCheck Ver(VersionStatus s = VersionStatus.NotInstalled, string? installed = null) =>
        new(s, installed, "1.0", new List<InstalledApp>());

    [Fact]
    public void Windows_cracked_blocks()
    {
        var r = RuleEvaluator.Evaluate(Report(win: LicenseVerdict.CrackSuspected), Pkg("block_if_windows_cracked"), Ver());
        Assert.False(r.Allowed);
        Assert.Equal(new[] { Strings.ReasonWindowsCracked }, r.Reasons);
    }

    [Fact]
    public void Windows_genuine_allowed()
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg("block_if_windows_cracked"), Ver());
        Assert.True(r.Allowed);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void Office_cracked_blocks()
    {
        var r = RuleEvaluator.Evaluate(Report(office: LicenseVerdict.CrackSuspected), Pkg("block_if_office_cracked"), Ver());
        Assert.False(r.Allowed);
        Assert.Equal(new[] { Strings.ReasonOfficeCracked }, r.Reasons);
    }

    [Fact]
    public void Office_not_installed_allowed()
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg("block_if_office_cracked"), Ver());
        Assert.True(r.Allowed);
    }

    [Theory]
    [InlineData(VersionStatus.Equal)]
    [InlineData(VersionStatus.Newer)]
    public void Installed_gte_blocks(VersionStatus s)
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg("block_if_installed_version_gte"), Ver(s, "2.0"));
        Assert.False(r.Allowed);
        Assert.Equal(new[] { string.Format(Strings.ReasonInstalledGte, "2.0") }, r.Reasons);
        Assert.False(r.NeedsDowngradeConfirm);
    }

    [Theory]
    [InlineData(VersionStatus.Older)]
    [InlineData(VersionStatus.NotInstalled)]
    public void Installed_older_or_missing_allowed(VersionStatus s)
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg("block_if_installed_version_gte"), Ver(s, "0.5"));
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Keyword_files_block()
    {
        var r = RuleEvaluator.Evaluate(Report(keywordHits: 3), Pkg("block_if_keyword_file"), Ver());
        Assert.False(r.Allowed);
        Assert.Equal(new[] { string.Format(Strings.ReasonKeywordFiles, 3) }, r.Reasons);
    }

    [Fact]
    public void No_keyword_files_allowed()
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg("block_if_keyword_file"), Ver());
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Require_internet_blocks_when_offline()
    {
        var r = RuleEvaluator.Evaluate(Report(internet: false), Pkg("require_internet"), Ver());
        Assert.False(r.Allowed);
        Assert.Equal(new[] { Strings.ReasonRequireInternet }, r.Reasons);
    }

    [Fact]
    public void Require_internet_allowed_when_online()
    {
        var r = RuleEvaluator.Evaluate(Report(internet: true), Pkg("require_internet"), Ver());
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Multiple_reasons_in_rule_order()
    {
        var r = RuleEvaluator.Evaluate(Report(win: LicenseVerdict.CrackSuspected, internet: false),
            Pkg("require_internet", "block_if_windows_cracked", "REQUIRE_INTERNET"), Ver());
        Assert.False(r.Allowed);
        Assert.Equal(new[] { Strings.ReasonRequireInternet, Strings.ReasonWindowsCracked }, r.Reasons);
    }

    [Fact]
    public void Newer_without_gte_rule_needs_downgrade_confirm()
    {
        var r = RuleEvaluator.Evaluate(Report(), Pkg(), Ver(VersionStatus.Newer, "2.0"));
        Assert.True(r.Allowed);
        Assert.True(r.NeedsDowngradeConfirm);
    }

    [Fact]
    public void Unknown_rule_type_is_ignored()
    {
        var r = RuleEvaluator.Evaluate(Report(internet: false), Pkg("bogus", "", null), Ver());
        Assert.True(r.Allowed);
        Assert.Empty(r.Reasons);
    }
}
