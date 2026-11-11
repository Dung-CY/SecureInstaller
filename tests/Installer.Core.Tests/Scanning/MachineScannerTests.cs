using Installer.Core.Licensing;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;

namespace Installer.Core.Tests.Scanning;

public class MachineScannerTests
{
    private sealed class SyncProgress : IProgress<string>
    {
        public List<string> Steps { get; } = new();
        public void Report(string value) => Steps.Add(value);
    }

    [Fact]
    public async Task MachineScanner_reports_four_steps_in_order()
    {
        var wmi = new FakeWmi(); var reg = new FakeRegistry(); var sys = new FakeSystemInfo();
        var ms = new MachineScanner(new HardwareScanner(wmi, sys), new NetworkHistoryScanner(reg, new FakeProbe(true)),
            new LicenseScanner(wmi, reg, sys), new KeywordFileScanner());
        var p = new SyncProgress();
        var report = await ms.ScanAsync(Catalog.Empty(), p, CancellationToken.None);
        Assert.Equal(new[] { "Đang quét phần cứng", "Đang đọc lịch sử mạng", "Đang kiểm tra bản quyền", "Đang tìm file theo từ khóa" }, p.Steps);
        Assert.True(report.Network.InternetNow);
        Assert.NotNull(report.Keywords);
    }

    [Fact]
    public async Task MachineScanner_survives_failing_subscanner()
    {
        var wmi = new FakeWmi();
        foreach (var c in new[] { "Win32_ComputerSystem", "Win32_BIOS", "Win32_DiskDrive", "Win32_NetworkAdapter", "SoftwareLicensingProduct", "OfficeSoftwareProtectionProduct" })
            wmi.Throws.Add(c);
        var reg = new FakeRegistry(); var sys = new FakeSystemInfo();
        var ms = new MachineScanner(new HardwareScanner(wmi, sys), new NetworkHistoryScanner(reg, new FakeProbe()),
            new LicenseScanner(wmi, reg, sys), new KeywordFileScanner());
        var report = await ms.ScanAsync(Catalog.Empty(), null, CancellationToken.None);
        Assert.Equal(LicenseVerdict.Unknown, report.Windows.Verdict);
    }

    private static MachineScanner Build(FakeProbe probe)
    {
        var wmi = new FakeWmi(); var reg = new FakeRegistry(); var sys = new FakeSystemInfo();
        return new MachineScanner(new HardwareScanner(wmi, sys), new NetworkHistoryScanner(reg, probe),
            new LicenseScanner(wmi, reg, sys), new KeywordFileScanner());
    }

    [Fact]
    public async Task Network_phase_exception_falls_back_and_report_is_produced()
    {
        var ms = Build(new FakeProbe(true) { Throw = new InvalidOperationException("boom") });
        var report = await ms.ScanAsync(Catalog.Empty(), null, CancellationToken.None);
        Assert.Empty(report.Network.Profiles);
        Assert.False(report.Network.InternetNow);
        Assert.False(string.IsNullOrEmpty(report.Hardware.Hostname));
        Assert.NotNull(report.Windows);
        Assert.NotNull(report.Keywords);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_from_MachineScanner()
    {
        var ms = Build(new FakeProbe(true));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ms.ScanAsync(Catalog.Empty(), null, cts.Token));
    }
}
