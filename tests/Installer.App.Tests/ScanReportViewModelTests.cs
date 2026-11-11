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
    public async Task Scan_errors_are_localized_without_raw_text()
    {
        var r = Report(LicenseVerdict.Genuine);
        r = r with { Hardware = r.Hardware with { Errors = new() { "Win32_BIOS: Access denied secret", "NoColon" } } };
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(r), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        var rows = vm.HardwareRows.Where(x => x.Label == "Lỗi quét").Select(x => x.Value).ToList();
        Assert.Equal(new[] { "Win32_BIOS: Không đọc được dữ liệu", "NoColon: Không đọc được dữ liệu" }, rows);
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

    static ScanReport ReportWithDevices() => Report(LicenseVerdict.Suspicious) is var r
        ? r with
        {
            Hardware = r.Hardware with
            {
                Peripherals = new()
                {
                    new("Logitech Mouse", "Mouse", @"HID\VID_046D&PID_C077"),
                    new("HID Keyboard", "Keyboard", @"HID\VID_1234"),
                    new("Kingston DataTraveler", "DiskDrive", @"USBSTOR\DISK&VEN_KINGSTON"),
                    new("Canon LBP2900", "Printer", @"SWD\PRINTENUM\{ABC}"),
                    new("Unknown gadget", null, @"USB\VID_9999"),
                }
            },
            Keywords = new KeywordScanResult(new() { "a", "b" }, false, false),
        }
        : r;

    [Fact]
    public async Task Overview_has_five_items_with_warnings()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(ReportWithDevices()), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Windows", "Office", "Internet", "File từ khóa", "Thiết bị" },
            vm.Overview.Select(o => o.Label));
        Assert.Equal(new OverviewItem("Windows", "Nghi vấn", true), vm.Overview[0]);
        Assert.Equal(new OverviewItem("Office", "Chính hãng", false), vm.Overview[1]);
        Assert.Equal(new OverviewItem("Internet", "Có", false), vm.Overview[2]);
        Assert.Equal(new OverviewItem("File từ khóa", "2", true), vm.Overview[3]);
        Assert.Equal(new OverviewItem("Thiết bị", "5", false), vm.Overview[4]);

        var clean = Report(LicenseVerdict.CrackSuspected) with
        {
            Network = new NetworkHistory(new(), false),
            Keywords = new KeywordScanResult(new(), false, false),
        };
        var vm2 = new ScanReportViewModel((_, _) => Task.FromResult(clean), _ => { });
        await vm2.ScanCommand.ExecuteAsync(null);
        Assert.True(vm2.Overview[0].IsWarning);
        Assert.Equal("Có dấu hiệu crack", vm2.Overview[0].Value);
        Assert.Equal("Không", vm2.Overview[2].Value);
        Assert.Equal(new OverviewItem("File từ khóa", "0", false), vm2.Overview[3]);
    }

    [Fact]
    public async Task Peripherals_grouped_collapsed_by_default_with_counts()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(ReportWithDevices()), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Bàn phím & chuột", "Lưu trữ USB", "Máy in", "Khác" },
            vm.PeripheralGroups.Select(g => g.Label));
        Assert.Equal(new[] { 2, 1, 1, 1 }, vm.PeripheralGroups.Select(g => g.Count));
        Assert.All(vm.PeripheralGroups, g => Assert.False(g.IsExpanded));
        Assert.Equal(new[] { "Logitech Mouse", "HID Keyboard" }, vm.PeripheralGroups[0].Items.Select(d => d.Name));
        // The computer tab no longer lists every device.
        Assert.DoesNotContain(vm.HardwareRows, r => r.Value == "Logitech Mouse");
    }

    [Fact]
    public async Task Device_search_filters_and_expands_matching_groups()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(ReportWithDevices()), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);

        vm.DeviceSearch = "kingston";       // matches Name, case-insensitive
        var usb = vm.PeripheralGroups.Single(g => g.Label == "Lưu trữ USB");
        Assert.Equal(1, usb.Count);
        Assert.True(usb.IsExpanded);
        Assert.All(vm.PeripheralGroups.Where(g => g != usb), g => { Assert.Equal(0, g.Count); Assert.False(g.IsExpanded); });
        Assert.False(vm.NoDeviceMatches);

        vm.DeviceSearch = "vid_046d";       // matches DeviceId only
        var km = vm.PeripheralGroups[0];
        Assert.Equal(1, km.Count);
        Assert.Equal("Logitech Mouse", Assert.Single(km.Items).Name);
        Assert.True(km.IsExpanded);
        Assert.False(usb.IsExpanded);

        vm.DeviceSearch = "zzz";
        Assert.True(vm.NoDeviceMatches);
        Assert.All(vm.PeripheralGroups, g => Assert.Equal(0, g.Count));

        vm.DeviceSearch = "";
        Assert.False(vm.NoDeviceMatches);
        Assert.Equal(new[] { 2, 1, 1, 1 }, vm.PeripheralGroups.Select(g => g.Count));
        Assert.All(vm.PeripheralGroups, g => Assert.False(g.IsExpanded));
    }

    [Fact]
    public void ForReport_is_read_only_and_back_calls_onBack()
    {
        var backs = 0;
        var vm = ScanReportViewModel.ForReport(ReportWithDevices(), "Lịch sử quét — PC-01 — 10/11/2026 14:30", () => backs++);
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.IsScanning);
        Assert.Equal("Lịch sử quét — PC-01 — 10/11/2026 14:30", vm.Title);
        Assert.Equal("Quay lại", vm.ContinueText);
        Assert.Equal(5, vm.Overview.Count);
        Assert.Equal(4, vm.PeripheralGroups.Count);
        Assert.Contains(vm.HardwareRows, r => r.Label == "Serial BIOS" && r.Value == "SN1");
        Assert.True(vm.ContinueCommand.CanExecute(null));
        vm.ContinueCommand.Execute(null);
        Assert.Equal(1, backs);
    }

    [Fact]
    public void Normal_instance_has_default_title_and_continue_text()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(Report(LicenseVerdict.Genuine)), _ => { });
        Assert.Equal("Báo cáo quét", vm.Title);
        Assert.Equal("Tiếp tục", vm.ContinueText);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public void ForReport_tolerates_null_parts_and_null_device_id()
    {
        var hw = new HardwareInfo(null!, null, null, null, null!, null!,
            new() { new("No id", null, null!), new(null!, "Mouse", @"HID\X") }, null!);
        var r = new ScanReport(DateTimeOffset.Now, hw, new NetworkHistory(null!, false), null!, null!,
            new KeywordScanResult(null!, false, false));
        var vm = ScanReportViewModel.ForReport(r, "t", () => { });
        Assert.Equal("Không xác định", vm.Overview[0].Value);
        Assert.False(vm.Overview[0].IsWarning);
        Assert.Equal("Không xác định", vm.Overview[1].Value);
        Assert.Equal("0", vm.Overview[3].Value);
        Assert.Equal("2", vm.Overview[4].Value);
        Assert.Contains("Không xác định", vm.WindowsLicenseText);
        Assert.Empty(vm.NetworkRows);
        Assert.Empty(vm.KeywordFiles);
        Assert.Equal(2, vm.PeripheralGroups.Sum(g => g.Count));
        vm.DeviceSearch = "no";
        Assert.Equal(1, vm.PeripheralGroups.Sum(g => g.Count));

        // Whole sections missing.
        var r2 = new ScanReport(DateTimeOffset.Now, null!, null!, null!, null!, null!);
        var vm2 = ScanReportViewModel.ForReport(r2, "t", () => { });
        Assert.Equal(5, vm2.Overview.Count);
        Assert.Empty(vm2.PeripheralGroups);
        Assert.Equal("Không", vm2.Overview[2].Value);
    }

    [Fact]
    public async Task ForReport_never_calls_scan()
    {
        var r = ReportWithDevices();
        var vm = ScanReportViewModel.ForReport(r, "t", () => { });
        var step = vm.CurrentStep;
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.False(vm.IsScanning);
        Assert.Same(r, vm.Report);
        Assert.Equal(step, vm.CurrentStep);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Search_does_not_mutate_source_peripherals()
    {
        var r = ReportWithDevices();
        var before = r.Hardware.Peripherals.ToList();
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(r), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        vm.DeviceSearch = "kingston";
        vm.DeviceSearch = "zzz";
        vm.DeviceSearch = "";
        Assert.Equal(before, r.Hardware.Peripherals);
    }

    [Fact]
    public async Task Group_header_text_includes_count_and_follows_search()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(ReportWithDevices()), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        var km = vm.PeripheralGroups[0];
        Assert.Equal("Bàn phím & chuột, 2 thiết bị", km.HeaderText);
        var changed = new List<string?>();
        km.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.DeviceSearch = "logitech";
        Assert.Equal("Bàn phím & chuột, 1 thiết bị", km.HeaderText);
        Assert.Contains(nameof(PeripheralGroup.HeaderText), changed);
    }

    [Fact]
    public async Task Unchanged_match_set_does_not_rebuild_items()
    {
        var vm = new ScanReportViewModel((_, _) => Task.FromResult(ReportWithDevices()), _ => { });
        await vm.ScanCommand.ExecuteAsync(null);
        var km = vm.PeripheralGroups[0];
        var resets = 0;
        km.Items.CollectionChanged += (_, _) => resets++;
        vm.DeviceSearch = "h";      // both keyboard/mouse devices still match (HID\ ids)
        Assert.Equal(0, resets);
        Assert.Equal(2, km.Count);
    }
}
