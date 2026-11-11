using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.App.Converters;
using Installer.Core.Licensing;
using Installer.Core.Scanning;

namespace Installer.App.ViewModels;

public record HardwareRow(string Label, string Value);
public record NetworkRow(string Name, string Kind, string First, string Last);
public record OverviewItem(string Label, string Value, bool IsWarning);

/// <summary>One peripheral category. Items holds the devices matching the current search.</summary>
public partial class PeripheralGroup : ObservableObject
{
    internal PeripheralGroup(DeviceCategory category, IReadOnlyList<DeviceInfo> all)
    {
        Category = category;
        Label = PeripheralCategorizer.Label(category);
        All = all;
        Items = new ObservableCollection<DeviceInfo>(all);
        _count = all.Count;
    }

    public DeviceCategory Category { get; }
    public string Label { get; }
    internal IReadOnlyList<DeviceInfo> All { get; }
    public ObservableCollection<DeviceInfo> Items { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HeaderText))] private int _count;
    public string HeaderText => $"{Label}, {Count} thiết bị";
    [ObservableProperty] private bool _isExpanded;

    internal void Filter(string text)
    {
        var matches = text.Length == 0
            ? All
            : All.Where(d => (d.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)
                          || (d.DeviceId ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!matches.SequenceEqual(Items))   // keep the realized containers when nothing changed
        {
            Items.Clear();
            foreach (var d in matches) Items.Add(d);
        }
        Count = Items.Count;
    }
}

public partial class ScanReportViewModel : ObservableObject
{
    public const string DefaultTitle = "Báo cáo quét";

    private readonly Func<IProgress<string>, CancellationToken, Task<ScanReport>>? _scan;
    private readonly Action<ScanReport> _onContinue;

    public ScanReportViewModel(Func<IProgress<string>, CancellationToken, Task<ScanReport>> scan, Action<ScanReport> onContinue)
    {
        _scan = scan; _onContinue = onContinue;
    }

    private ScanReportViewModel(ScanReport report, string title, Action onBack)
    {
        _onContinue = _ => onBack();
        IsReadOnly = true;
        Title = title;
        ContinueText = "Quay lại";
        CurrentStep = "Quét lúc " + report.ScannedAt.ToLocalTime().ToString(DisplayText.DateFormat);
        Apply(report);
    }

    /// <summary>Read-only view of an existing report: no scan runs and nothing is logged.</summary>
    public static ScanReportViewModel ForReport(ScanReport report, string title, Action onBack) =>
        new(report, title, onBack);

    public bool IsReadOnly { get; }
    public string Title { get; } = DefaultTitle;
    public string ContinueText { get; } = "Tiếp tục";

    [ObservableProperty] private string _currentStep = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueCommand))] private bool _isScanning;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ContinueCommand))] private ScanReport? _report;
    [ObservableProperty] private string _internetText = "";
    [ObservableProperty] private string _windowsLicenseText = "";
    [ObservableProperty] private string _officeLicenseText = "";
    [ObservableProperty] private bool _windowsIsCracked;
    [ObservableProperty] private bool _officeIsCracked;
    [ObservableProperty] private string? _keywordWarning;

    public ObservableCollection<HardwareRow> HardwareRows { get; } = new();
    public ObservableCollection<NetworkRow> NetworkRows { get; } = new();
    public ObservableCollection<string> KeywordFiles { get; } = new();
    public ObservableCollection<OverviewItem> Overview { get; } = new();
    public ObservableCollection<PeripheralGroup> PeripheralGroups { get; } = new();

    [ObservableProperty] private string _deviceSearch = "";
    [ObservableProperty] private bool _noDeviceMatches;

    partial void OnDeviceSearchChanged(string value) => ApplyDeviceSearch();

    private void ApplyDeviceSearch()
    {
        var text = (DeviceSearch ?? "").Trim();
        foreach (var g in PeripheralGroups)
        {
            g.Filter(text);
            g.IsExpanded = text.Length > 0 && g.Count > 0;
        }
        NoDeviceMatches = text.Length > 0 && PeripheralGroups.All(g => g.Count == 0);
    }

    private bool _done;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (_scan is null) return;   // read-only re-view never scans
        IsScanning = true;
        _done = false;
        Error = null;
        CurrentStep = "Đang chuẩn bị";
        try
        {
            var progress = new Progress<string>(s => { if (!_done) CurrentStep = s; });
            var r = await _scan(progress, CancellationToken.None);
            Apply(r);
        }
        catch (Exception ex)
        {
            Error = ErrorText.ForUser(ex);
        }
        finally
        {
            _done = true;
            CurrentStep = Error is null ? "Hoàn tất" : "Quét thất bại";
            IsScanning = false;
        }
    }

    private bool CanContinue() => Report is not null && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (Report is { } r) _onContinue(r);
    }

    // Reports re-opened from history are deserialized JSON, so any part may be missing.
    private void Apply(ScanReport r)
    {
        HardwareRows.Clear();
        var h = r.Hardware;
        var disks = h?.Disks ?? new();
        var nics = h?.Nics ?? new();
        var errors = h?.Errors ?? new();
        var peripherals = (h?.Peripherals ?? new())
            .Where(d => d is not null)
            .Select(d => d.DeviceId is null ? d with { DeviceId = "" } : d)
            .ToList();
        HardwareRows.Add(new("Tên máy", h?.Hostname ?? "—"));
        HardwareRows.Add(new("Hãng", h?.Manufacturer ?? "—"));
        HardwareRows.Add(new("Model", h?.Model ?? "—"));
        HardwareRows.Add(new("Serial BIOS", h?.BiosSerial ?? "—"));
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            if (d is null) continue;
            HardwareRows.Add(new($"Ổ cứng {i + 1}",
                string.Join(" · ", new[] { d.Model, d.Serial, d.InterfaceType }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }
        foreach (var n in nics.Where(n => n is not null)) HardwareRows.Add(new($"MAC {n.Name}", n.Mac ?? "—"));
        HardwareRows.Add(new("Thiết bị ngoại vi", $"{peripherals.Count} (xem tab Thiết bị ngoại vi)"));
        foreach (var e in errors.Where(e => e is not null)) {
            var i = e.IndexOf(':');
            HardwareRows.Add(new("Lỗi quét", $"{(i > 0 ? e[..i] : e)}: Không đọc được dữ liệu"));
        }

        NetworkRows.Clear();
        foreach (var p in (r.Network?.Profiles ?? new()).Where(p => p is not null))
            NetworkRows.Add(new(p.Name ?? "—", DisplayText.Of(p.Kind), DisplayText.Of(p.FirstConnected), DisplayText.Of(p.LastConnected)));
        var internet = r.Network?.InternetNow ?? false;
        InternetText = "Đang có internet: " + (internet ? "Có" : "Không");

        var winVerdict = r.Windows?.Verdict ?? LicenseVerdict.Unknown;
        var officeVerdict = r.Office?.Verdict ?? LicenseVerdict.Unknown;
        WindowsLicenseText = Format(r.Windows);
        OfficeLicenseText = Format(r.Office);
        WindowsIsCracked = winVerdict == LicenseVerdict.CrackSuspected;
        OfficeIsCracked = officeVerdict == LicenseVerdict.CrackSuspected;

        var files = r.Keywords?.MatchedFiles ?? new();
        KeywordFiles.Clear();
        foreach (var f in files.Where(f => f is not null)) KeywordFiles.Add(f);
        var warn = new List<string>();
        if (r.Keywords?.TimedOut == true) warn.Add("Đã hết thời gian quét, kết quả có thể chưa đầy đủ");
        if (r.Keywords?.Truncated == true) warn.Add("Chỉ hiển thị 1000 file đầu tiên");
        KeywordWarning = warn.Count == 0 ? null : string.Join(Environment.NewLine, warn);

        PeripheralGroups.Clear();
        foreach (var g in peripherals.GroupBy(PeripheralCategorizer.Categorize).OrderBy(g => g.Key))
            PeripheralGroups.Add(new PeripheralGroup(g.Key, g.ToList()));
        ApplyDeviceSearch();

        Overview.Clear();
        Overview.Add(new("Windows", DisplayText.Of(winVerdict), IsLicenseWarning(winVerdict)));
        Overview.Add(new("Office", DisplayText.Of(officeVerdict), IsLicenseWarning(officeVerdict)));
        Overview.Add(new("Internet", internet ? "Có" : "Không", false));
        Overview.Add(new("File từ khóa", KeywordFiles.Count.ToString(), KeywordFiles.Count > 0));
        Overview.Add(new("Thiết bị", peripherals.Count.ToString(), false));
        Report = r;
    }

    private static bool IsLicenseWarning(LicenseVerdict v) =>
        v is LicenseVerdict.CrackSuspected or LicenseVerdict.Suspicious;

    private static string Format(LicenseInfo? l)
    {
        if (l is null) return $"Kết luận: {DisplayText.Of(LicenseVerdict.Unknown)}";
        var lines = new List<string>
        {
            $"Sản phẩm: {l.Product}" + (string.IsNullOrWhiteSpace(l.Edition) ? "" : $" ({l.Edition})"),
            $"Kênh: {l.Channel}",
            $"Kích hoạt: {(l.Activated is null ? "—" : l.Activated.Value ? "Có" : "Không")}",
            $"Kết luận: {DisplayText.Of(l.Verdict)}",
        };
        lines.AddRange((l.Evidence ?? new()).Select(e => "• " + e));
        return string.Join(Environment.NewLine, lines);
    }
}
