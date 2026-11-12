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

    [Fact]
    public void FindForPackage_includes_marker_entry()
    {
        var reg = new FakeRegistry();
        var marker = new Installer.Core.Install.InstallMarker(reg, reg);
        marker.Write("zip1", "2.0", DateTimeOffset.UtcNow);
        var pkg = new PackageEntry("zip1", "Zip App", "2.0", InstallerType.Zip, "a.zip", "", null, 60,
            new DetectRule(null, "^NoSuch$"), new(), new(), new(), "b", "s", "k");
        var r = new InstalledAppsScanner(reg, marker).FindForPackage(pkg);
        var a = Assert.Single(r);
        Assert.True(a.IsMarker);
        Assert.Equal("marker:zip1", a.KeyName);
        Assert.Equal("2.0", a.DisplayVersion);
        Assert.Empty(new InstalledAppsScanner(reg).FindForPackage(pkg));
    }

    [Fact]
    public void Uninstall_fields_are_read_as_strings_and_dword_ignored()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\App", new()
        {
            ["DisplayName"] = "App", ["UninstallString"] = "u.exe /x", ["QuietUninstallString"] = 5,
            ["DisplayIcon"] = "i.ico",
        });
        var a = Assert.Single(new InstalledAppsScanner(reg).Find(new DetectRule(null, "App")));
        Assert.Equal("u.exe /x", a.UninstallString);
        Assert.Null(a.QuietUninstallString);
        Assert.Equal("i.ico", a.DisplayIcon);
        Assert.False(a.IsMarker);
    }

    [Fact]
    public void Marker_ignored_when_detect_rule_matches()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\App", V("App", "5.0"));
        var marker = new Installer.Core.Install.InstallMarker(reg, reg);
        marker.Write("p1", "1.0", DateTimeOffset.UtcNow);
        var pkg = new PackageEntry("p1", "App", "5.0", InstallerType.Zip, "a.zip", "", null, 60,
            new DetectRule(null, "^App$"), new(), new(), new(), "b", "s", "k");
        var a = Assert.Single(new InstalledAppsScanner(reg, marker).FindForPackage(pkg));
        Assert.False(a.IsMarker);
        Assert.Equal("5.0", a.DisplayVersion);
    }

    [Fact]
    public void FindForPackage_returns_marker_key_row_first_without_duplicates()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\Real", V("Foo Pro", "2.0"));
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\Other", V("Foo Other", "1.0"));
        var marker = new Installer.Core.Install.InstallMarker(reg, reg);
        marker.Write("foo", "2.0", DateTimeOffset.UtcNow, "32|Real");
        var pkg = new PackageEntry("foo", "Foo", "2.0", InstallerType.Exe, "a.exe", "", null, 60, new DetectRule(null, "^Foo"),
            new(), new(), new(), "b", "s", "k");
        var r = new InstalledAppsScanner(reg, marker).FindForPackage(pkg);
        Assert.Equal(new[] { "Real", "Other" }, r.Select(a => a.KeyName));
        Assert.All(r, a => Assert.False(a.IsMarker));
    }

    [Fact]
    public void Snapshot_is_hklm_only_and_PickNewKey_follows_the_rules()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\Old", V("Old", "1"));
        reg.AddKey(RegistryHive.CurrentUser, RegistryView.Registry64, U + @"\User", V("User", "1"));
        var s = new InstalledAppsScanner(reg);
        var before = s.SnapshotMachineKeys();
        Assert.Equal(new[] { "64|Old" }, before);

        // none new: falls back to the Find(Detect) row
        Assert.Equal("64|Old", s.PickNewKey(before, new DetectRule(null, "^Old")));
        // one new: that key
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\A", new() { ["DisplayName"] = "Zed Helper", ["UninstallString"] = "x" });
        Assert.Equal("32|A", s.PickNewKey(before, new DetectRule(null, "^Foo")));
        // several: regex match wins over the first
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\B", V("Foo App", "1"));
        Assert.Equal("64|B", s.PickNewKey(before, new DetectRule(null, "^Foo")));
        // several, no regex match: first with an UninstallString
        Assert.Equal("32|A", s.PickNewKey(before, new DetectRule(null, "^Nope")));
    }

    static PackageEntry Pk(string id, string name, string ver, string? regex) =>
        new(id, name, ver, InstallerType.Exe, "a.exe", "", null, 60, new DetectRule(null, regex), new(), new(), new(), "b", "s", "k");

    [Fact]
    public void Burn_bundle_install_picks_the_visible_bundle_not_the_hidden_child()
    {
        var reg = new FakeRegistry();
        var s = new InstalledAppsScanner(reg);
        var before = s.SnapshotMachineKeys();
        const string DN = "Microsoft Windows Desktop Runtime - 8.0.11 (x64)";
        // child MSI: hidden (SystemComponent=1), registered first
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\{5F1E0000-0000-0000-0000-000000000001}",
            new() { ["DisplayName"] = DN, ["DisplayVersion"] = "8.0.11", ["SystemComponent"] = 1, ["UninstallString"] = "MsiExec.exe /X{5F1E0000-0000-0000-0000-000000000001}" });
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, U + @"\{5F1E0000-0000-0000-0000-000000000002}", new() { ["SystemComponent"] = 1 });
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\{de6c0000-0000-0000-0000-000000000003}",
            new() { ["DisplayName"] = DN, ["DisplayVersion"] = "8.0.11.34123", ["QuietUninstallString"] = "\"C:\\ProgramData\\Package Cache\\{de6c}\\runtime.exe\" /uninstall /quiet" });
        Assert.Equal("32|{de6c0000-0000-0000-0000-000000000003}", s.PickNewKey(before, new DetectRule(null, "Desktop Runtime"), Pk("net", ".NET Desktop Runtime", "8.0.11", null)));

        // Find skips the hidden child, but an exact ProductCode still counts
        var found = Assert.Single(s.Find(new DetectRule(null, "Desktop Runtime")));
        Assert.Equal("{de6c0000-0000-0000-0000-000000000003}", found.KeyName);
        Assert.True(Assert.Single(s.Find(new DetectRule("{5F1E0000-0000-0000-0000-000000000001}", null))).SystemComponent);
    }

    [Fact]
    public void Preinstalled_app_with_a_different_display_name_is_found_by_version_and_shared_word()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\Bit4id - Universal MW (x)",
            new() { ["DisplayName"] = "Bit4id - Universal MW 1.4.10.839", ["DisplayVersion"] = "1.4.10.839", ["UninstallString"] = "\"C:\\Program Files (x86)\\Bit4id\\uninst.exe\"" });
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, U + @"\Other", V("Universal Thing", "9.9"));
        var s = new InstalledAppsScanner(reg);
        var pkg = Pk("bit4id", "Bit4id Universal MW", "1.4.10.839", "^Bit4id Universal MW");
        var row = Assert.Single(s.FindForPackage(pkg));
        Assert.Equal("Bit4id - Universal MW (x)", row.KeyName);
        Assert.False(row.IsMarker);
        // no new key after the install: same fallback
        Assert.Equal("32|Bit4id - Universal MW (x)", s.PickNewKey(s.SnapshotMachineKeys(), pkg.Detect, pkg));
        // a different version does not match
        Assert.Empty(s.FindForPackage(pkg with { Version = "2.0" }));
    }
}
