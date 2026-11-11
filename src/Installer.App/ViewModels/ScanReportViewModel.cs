using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.App.Converters;
using Installer.Core.Licensing;
using Installer.Core.Scanning;

namespace Installer.App.ViewModels;

public record HardwareRow(string Label, string Value);
public record NetworkRow(string Name, string Kind, string First, string Last);

public partial class ScanReportViewModel : ObservableObject
{
    private readonly Func<IProgress<string>, CancellationToken, Task<ScanReport>> _scan;
    private readonly Action<ScanReport> _onContinue;

    public ScanReportViewModel(Func<IProgress<string>, CancellationToken, Task<ScanReport>> scan, Action<ScanReport> onContinue)
    {
        _scan = scan; _onContinue = onContinue;
    }

    [ObservableProperty] private string _currentStep = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueCommand))] private bool _isScanning;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueCommand))] private ScanReport? _report;
    [ObservableProperty] private string _internetText = "";
    [ObservableProperty] private string _windowsLicenseText = "";
    [ObservableProperty] private string _officeLicenseText = "";
    [ObservableProperty] private bool _windowsIsCracked;
    [ObservableProperty] private bool _officeIsCracked;

    public ObservableCollection<HardwareRow> HardwareRows { get; } = new();
    public ObservableCollection<NetworkRow> NetworkRows { get; } = new();
    public ObservableCollection<string> KeywordFiles { get; } = new();

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        Error = null;
        CurrentStep = "Đang chuẩn bị";
        try
        {
            var progress = new Progress<string>(s => CurrentStep = s);
            var r = await _scan(progress, CancellationToken.None);
            Apply(r);
            CurrentStep = "Hoàn tất";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            CurrentStep = "Quét thất bại";
        }
        finally { IsScanning = false; }
    }

    private bool CanContinue() => Report is not null && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (Report is { } r) _onContinue(r);
    }

    private void Apply(ScanReport r)
    {
        HardwareRows.Clear();
        var h = r.Hardware;
        HardwareRows.Add(new("Tên máy", h.Hostname));
        HardwareRows.Add(new("Hãng", h.Manufacturer ?? "—"));
        HardwareRows.Add(new("Model", h.Model ?? "—"));
        HardwareRows.Add(new("Serial BIOS", h.BiosSerial ?? "—"));
        for (int i = 0; i < h.Disks.Count; i++)
        {
            var d = h.Disks[i];
            HardwareRows.Add(new($"Ổ cứng {i + 1}",
                string.Join(" · ", new[] { d.Model, d.Serial, d.InterfaceType }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }
        foreach (var n in h.Nics) HardwareRows.Add(new($"MAC {n.Name}", n.Mac));
        HardwareRows.Add(new("Thiết bị ngoại vi", h.Peripherals.Count.ToString()));
        foreach (var p in h.Peripherals) HardwareRows.Add(new("", p.Name));
        foreach (var e in h.Errors) HardwareRows.Add(new("Lỗi quét", e));

        NetworkRows.Clear();
        foreach (var p in r.Network.Profiles)
            NetworkRows.Add(new(p.Name, DisplayText.Of(p.Kind), DisplayText.Of(p.FirstConnected), DisplayText.Of(p.LastConnected)));
        InternetText = "Đang có internet: " + (r.Network.InternetNow ? "Có" : "Không");

        WindowsLicenseText = Format(r.Windows);
        OfficeLicenseText = Format(r.Office);
        WindowsIsCracked = r.Windows.Verdict == LicenseVerdict.CrackSuspected;
        OfficeIsCracked = r.Office.Verdict == LicenseVerdict.CrackSuspected;

        KeywordFiles.Clear();
        foreach (var f in r.Keywords.MatchedFiles) KeywordFiles.Add(f);
        Report = r;
    }

    private static string Format(LicenseInfo l)
    {
        var lines = new List<string>
        {
            $"Sản phẩm: {l.Product}" + (string.IsNullOrWhiteSpace(l.Edition) ? "" : $" ({l.Edition})"),
            $"Kênh: {l.Channel}",
            $"Kích hoạt: {(l.Activated is null ? "—" : l.Activated.Value ? "Có" : "Không")}",
            $"Kết luận: {DisplayText.Of(l.Verdict)}",
        };
        lines.AddRange(l.Evidence.Select(e => "• " + e));
        return string.Join(Environment.NewLine, lines);
    }
}
