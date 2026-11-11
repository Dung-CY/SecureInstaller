using Installer.Core.Model;
using Installer.Core.Tests.Fakes;
using Installer.Core.Versioning;
using Microsoft.Win32;

namespace Installer.Core.Tests.Versioning;

public class InstalledAppsScannerTests
{
    private const string U = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static Dictionary<string, object?> V(object? name, object? ver) =>
        new() { ["DisplayName"] = name, ["DisplayVersion"] = ver };

    [Fact]
    public void Find_matches_product_code_case_insensitive_in_wow64()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\{ABC-1}", V("7-Zip", "22.01"));
        var r = new InstalledAppsScanner(reg).Find(new DetectRule("{abc-1}", null));
        var a = Assert.Single(r);
        Assert.False(a.Is64Bit);
        Assert.False(a.PerUser);
        Assert.Equal("22.01", a.DisplayVersion);
    }

    [Fact]
    public void Find_matches_regex_in_hkcu()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.CurrentUser, RegistryView.Registry64, U + @"\x", V("Notepad++ (64-bit)", "8.6"));
        var r = new InstalledAppsScanner(reg).Find(new DetectRule(null, @"^notepad\+\+"));
        var a = Assert.Single(r);
        Assert.True(a.PerUser);
        Assert.True(a.Is64Bit);
    }

    [Fact]
    public void Find_dword_display_version_is_stringified()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\x", V("Foo", 5));
        Assert.Equal("5", Assert.Single(new InstalledAppsScanner(reg).Find(new DetectRule(null, "Foo"))).DisplayVersion);
    }

    [Fact]
    public void Find_skips_bad_display_name_and_handles_bad_regex_and_null_rule()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\a", V(5, "1"));
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\b", V("", "1"));
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\c", V("Foo", ""));
        var s = new InstalledAppsScanner(reg);
        Assert.Empty(s.Find(new DetectRule(null, "[unclosed")));
        Assert.Empty(s.Find(new DetectRule(null, null)));
        Assert.Empty(s.Find(new DetectRule("a", null)));
        Assert.Null(Assert.Single(s.Find(new DetectRule(null, "Foo"))).DisplayVersion);
    }
}
