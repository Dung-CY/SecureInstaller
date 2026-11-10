using Installer.Core.Licensing;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public sealed class MachineScanner
{
    private readonly HardwareScanner _hw;
    private readonly NetworkHistoryScanner _net;
    private readonly LicenseScanner _lic;
    private readonly KeywordFileScanner _kw;

    public MachineScanner(HardwareScanner hw, NetworkHistoryScanner net, LicenseScanner lic, KeywordFileScanner kw)
    {
        _hw = hw; _net = net; _lic = lic; _kw = kw;
    }

    public async Task<ScanReport> ScanAsync(Catalog catalog, IProgress<string>? step, CancellationToken ct)
    {
        step?.Report("Đang quét phần cứng");
        var hardware = await Task.Run(() => _hw.Scan(), ct).ConfigureAwait(false);

        step?.Report("Đang đọc lịch sử mạng");
        var network = await _net.ScanAsync(ct).ConfigureAwait(false);

        step?.Report("Đang kiểm tra bản quyền");
        var (win, office) = await Task.Run(() =>
            (_lic.ScanWindows(catalog.CrackSignatures), _lic.ScanOffice(catalog.CrackSignatures)), ct).ConfigureAwait(false);

        step?.Report("Đang tìm file theo từ khóa");
        var keywords = await Task.Run(() => _kw.Scan(catalog.KeywordScan, ct), ct).ConfigureAwait(false);

        return new ScanReport(DateTimeOffset.Now, hardware, network, win, office, keywords);
    }
}
