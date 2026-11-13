using Installer.Core.Versioning;

namespace Installer.Core.Tests.Versioning;

public class VersionComparerTests
{
    private static InstalledApp App(string? v, bool x64 = true) => new("App", v, "k", x64, false);

    [Theory]
    [InlineData("22.01", "24.08", -1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("10.0.19045", "10.0.9", 1)]
    [InlineData("2021 build 16.0.14332", "16.0.14332", 1)]
    [InlineData("v3.10", "3.9", 1)]
    [InlineData("1.0.0.1", "1.0", 1)]
    [InlineData("99999999999999999999999.1", "99999999999999999999998.9", 1)]
    [InlineData("007", "7", 0)]
    public void Compare_cases(string a, string b, int expected) =>
        Assert.Equal(expected, VersionComparer.Compare(a, b));

    [Fact]
    public void Compare_no_digits_returns_null()
    {
        Assert.Null(VersionComparer.Compare("latest", "1.0"));
        Assert.Null(VersionComparer.Compare("1.0", "latest"));
    }

    [Fact]
    public void Check_picks_highest_of_x86_and_x64()
    {
        var r = VersionComparer.Check(new[] { App("22.01", false), App("23.01") }, "24.08");
        Assert.Equal(VersionStatus.Older, r.Status);
        Assert.Equal("23.01", r.InstalledVersion);
        Assert.Equal(2, r.Matches.Count);
    }

    [Fact]
    public void Check_empty_is_NotInstalled()
    {
        var r = VersionComparer.Check(Array.Empty<InstalledApp>(), "1.0");
        Assert.Equal(VersionStatus.NotInstalled, r.Status);
        Assert.Null(r.InstalledVersion);
    }

    [Fact]
    public void Check_all_incomparable_is_Incomparable()
    {
        var r = VersionComparer.Check(new[] { App("latest"), App(null) }, "1.0");
        Assert.Equal(VersionStatus.Incomparable, r.Status);
        Assert.Equal("latest", r.InstalledVersion);
    }

    [Fact]
    public void Check_equal_and_newer()
    {
        Assert.Equal(VersionStatus.Equal, VersionComparer.Check(new[] { App("1.0") }, "1.0.0").Status);
        Assert.Equal(VersionStatus.Newer, VersionComparer.Check(new[] { App("2.0") }, "1.0").Status);
    }

    [Theory]
    [InlineData("8.0.34.36622", "8.0.34", VersionStatus.Equal)]   // publisher build number
    [InlineData("8.0.35.1", "8.0.34", VersionStatus.Newer)]
    [InlineData("8.0.34", "8.0", VersionStatus.Newer)]            // too short to treat the rest as a build number
    public void Build_number_suffix_counts_as_same_release(string installed, string pkg, VersionStatus expected) =>
        Assert.Equal(expected, VersionComparer.Check(new[] { App(installed) }, pkg).Status);
}
