using Installer.Core.Licensing;
using Installer.Core.Model;

namespace Installer.Core.Scanning;

public sealed class MachineScanner
{
    private readonly HardwareScanner _hw;
    private readonly NetworkHistoryScanner _net;
    private readonly LicenseScanner _lic;
    private readonly KeywordFileScanner _kw;
    private readonly UsbStorageHistoryScanner? _usb;

    public MachineScanner(HardwareScanner hw, NetworkHistoryScanner net, LicenseScanner lic, KeywordFileScanner kw, UsbStorageHistoryScanner? usb = null)
    {
        _hw = hw; _net = net; _lic = lic; _kw = kw; _usb = usb;
    }

    public async Task<ScanReport> ScanAsync(Catalog catalog, IProgress<string>? step, CancellationToken ct)
    {
        step?.Report("Đang quét phần cứng");
        HardwareInfo hardware;
        try { hardware = await Task.Run(() => _hw.Scan(), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hardware = new HardwareInfo(Environment.MachineName, null, null, null, new(), new(), new(),
                new() { "Hardware: " + ex.Message });
        }

        step?.Report("Đang đọc lịch sử mạng");
        NetworkHistory network;
        try { network = await _net.ScanAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { network = new NetworkHistory(new(), false); }

        step?.Report("Đang kiểm tra bản quyền");
        var win = await Task.Run(() => SafeLicense("Windows", () => _lic.ScanWindows(catalog.CrackSignatures)), ct).ConfigureAwait(false);
        var office = await Task.Run(() => SafeLicense("Office", () => _lic.ScanOffice(catalog.CrackSignatures)), ct).ConfigureAwait(false);

        step?.Report("Đang tìm file theo từ khóa");
        KeywordScanResult keywords;
        try { keywords = await Task.Run(() => _kw.Scan(catalog.KeywordScan, ct), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { keywords = new KeywordScanResult(new(), false, false); }

        step?.Report("Đang đọc lịch sử USB");
        var usbHistory = new List<UsbStorageRecord>();
        if (_usb is not null)
        {
            try { usbHistory = await Task.Run(() => _usb.Scan(), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        return new ScanReport(DateTimeOffset.Now, hardware, network, win, office, keywords, usbHistory);
    }

    private static LicenseInfo SafeLicense(string product, Func<LicenseInfo> scan)
    {
        try { return scan(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LicenseInfo(product, null, LicenseChannel.Unknown, null, null, null, LicenseVerdict.Unknown,
                new() { "Không đọc được thông tin bản quyền" });
        }
    }
}
