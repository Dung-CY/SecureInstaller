using Installer.App.Converters;
using Installer.App.ViewModels;
using Installer.Core.Licensing;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.Tests;

public class ScanReportViewModelTests
{
    static LicenseInfo Lic(string p, LicenseVerdict v) =>
        new(p, "Pro", LicenseChannel.VolumeKms, true, "XXXXX", "127.0.0.2", v, new() { "KMS lạ" });

    static ScanReport Report(LicenseVerdict win) => new(DateTimeOffset.Now,
        new HardwareInfo("PC1", "Dell", "Opti", "SN1", new() { new("Disk", "S", "SATA") },
            new() { new("Intel NIC", "AA:BB") }, new() { new("Mouse", "Mouse", "USB_X") }, new()),
        new NetworkHistory(new() { new("Cafe", NetworkKind.Wireless, new DateTime(2026, 1, 2, 3, 4, 0), null) }, true),
        Lic("Windows", win), Lic("Office", LicenseVerdict.Genuine),
        new KeywordScanResult(new() { @"C:\a\crack.exe" }, false, false));

    [Fact]
    public async Task Scan_populates_tabs_and_crack_flag()
    {
        ScanReport? cont = null;
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(Report(LicenseVerdict.CrackSuspected)), r => cont = r);
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Contains("Có dấu hiệu crack", vm.WindowsLicenseText);
        Assert.True(vm.WindowsIsCracked);
        Assert.False(vm.OfficeIsCracked);
        Assert.Contains("Chính hãng", vm.OfficeLicenseText);
        Assert.Contains(vm.HardwareRows, r => r.Label == "Serial BIOS" && r.Value == "SN1");
        Assert.Contains(vm.HardwareRows, r => r.Label == "MAC Intel NIC" && r.Value == "AA:BB");
        var n = Assert.Single(vm.NetworkRows);
        Assert.Equal("Wi-Fi", n.Kind);
        Assert.Equal("02/01/2026 03:04", n.First);
        Assert.Equal("—", n.Last);
        Assert.Single(vm.KeywordFiles);
        vm.ContinueCommand.Execute(null);
        Assert.NotNull(cont);
    }

    [Fact]
    public void DisplayText_maps_all_enum_values()
    {
        Assert.Equal("Chính hãng", DisplayText.Of(LicenseVerdict.Genuine));
        Assert.Equal("Nghi vấn", DisplayText.Of(LicenseVerdict.Suspicious));
        Assert.Equal("Có dấu hiệu crack", DisplayText.Of(LicenseVerdict.CrackSuspected));
        Assert.Equal("Không xác định", DisplayText.Of(LicenseVerdict.Unknown));
        Assert.Equal("Không cài", DisplayText.Of(LicenseVerdict.NotInstalled));
        Assert.Equal("Chưa cài", DisplayText.Of(VersionStatus.NotInstalled));
        Assert.Equal("Cũ hơn", DisplayText.Of(VersionStatus.Older));
        Assert.Equal("Bằng nhau", DisplayText.Of(VersionStatus.Equal));
        Assert.Equal("Mới hơn", DisplayText.Of(VersionStatus.Newer));
        Assert.Equal("Không so sánh được", DisplayText.Of(VersionStatus.Incomparable));
    }
}
