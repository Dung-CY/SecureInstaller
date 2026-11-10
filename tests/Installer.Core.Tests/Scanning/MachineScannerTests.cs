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
}
