using Installer.Core.Licensing;
using Installer.Core.Platform;
using Installer.Core.Tests.Fakes;
using Microsoft.Win32;

namespace Installer.Core.Tests.Licensing;

public class LicenseScannerTests
{
    private const string C2R = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";

    private static Dictionary<string, object?> Row(string name, object? status, string? desc = "desc", object? channel = null, string? kms = null) =>
        new() { ["Name"] = name, ["Description"] = desc, ["LicenseStatus"] = status, ["PartialProductKey"] = "XXXXX",
                ["ProductKeyChannel"] = channel, ["KeyManagementServiceMachine"] = kms,
                ["DiscoveredKeyManagementServiceMachineName"] = null };

    private static LicenseScanner Scanner(FakeWmi w, FakeRegistry? r = null, FakeSystemInfo? s = null) =>
        new(w, r ?? new FakeRegistry(), s ?? new FakeSystemInfo());

    [Fact]
    public void Scanner_null_ProductKeyChannel_uses_Description()
    {
        var w = new FakeWmi();
        w.Rows["SoftwareLicensingProduct"] = new() { Row("Windows(R), Pro edition", 1, "Windows(R) Operating System, VOLUME_KMSCLIENT channel", null, "kms.corp.local") };
        var r = Scanner(w, s: new FakeSystemInfo { IsDomainJoined = true }).ScanWindows(CrackSignatureDefaults.All);
        Assert.Equal(LicenseChannel.VolumeKms, r.Channel);
        Assert.Equal(LicenseVerdict.Genuine, r.Verdict);
        Assert.Equal("kms.corp.local", r.KmsHost);
    }

    [Fact]
    public void Scanner_wmi_throws_returns_Unknown_never_CrackSuspected()
    {
        var w = new FakeWmi();
        w.Throws.Add("SoftwareLicensingProduct");
        var win = Scanner(w).ScanWindows(CrackSignatureDefaults.All);
        Assert.Equal(LicenseVerdict.Unknown, win.Verdict);
        var off = Scanner(w).ScanOffice(CrackSignatureDefaults.All);
        Assert.Equal(LicenseVerdict.Unknown, off.Verdict);
    }

    [Fact]
    public void Office_c2r_only_maps_product()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, C2R,
            new() { ["ProductReleaseIds"] = "O365ProPlusRetail", ["VersionToReport"] = "16.0.1" });
        var r = Scanner(new FakeWmi(), reg).ScanOffice(CrackSignatureDefaults.All);
        Assert.Equal("O365ProPlusRetail", r.Product);
        Assert.Equal("16.0.1", r.Edition);
    }

    [Fact]
    public void Office_nothing_found_is_NotInstalled_and_picks_licensed_row()
    {
        Assert.Equal(LicenseVerdict.NotInstalled, Scanner(new FakeWmi()).ScanOffice(CrackSignatureDefaults.All).Verdict);
        var w = new FakeWmi();
        w.Rows["SoftwareLicensingProduct"] = new() { Row("A", 5u), Row("B", 1L, channel: "Retail") };
        var r = Scanner(w).ScanOffice(CrackSignatureDefaults.All);
        Assert.Equal("B", r.Product);
        Assert.Equal(LicenseVerdict.Genuine, r.Verdict);
    }

    [Fact]
    public void Real_windows_license_scan_does_not_throw()
    {
        var r = new LicenseScanner(new Wmi(), new RegistryReader(), new SystemInfo()).ScanWindows(CrackSignatureDefaults.All);
        Assert.NotNull(r);
        Assert.True(!string.IsNullOrEmpty(r.Product) || r.Verdict == LicenseVerdict.Unknown);
        Console.WriteLine($"verdict={r.Verdict} channel={r.Channel}");
    }
}
