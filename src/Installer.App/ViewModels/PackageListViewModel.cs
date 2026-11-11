using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core;
using Installer.Core.Errors;
using Installer.Core.Install;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.ViewModels;

public partial class PackageListViewModel : ObservableObject
{
    public const string InstallQueueTitle = "Cài đặt";
    public const string UninstallQueueTitle = "Gỡ cài đặt";

    private readonly Catalog _catalog;
    private readonly ScanReport _scan;
    private readonly Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> _install;
    private readonly Func<PackageEntry, IReadOnlyList<InstalledApp>, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> _uninstall;
    private readonly Func<PackageEntry, string, CancellationToken, Task<string>> _extractDoc;
    private readonly Func<string, bool> _confirm;
    private readonly Action<string> _openFile;
    private readonly Action<object> _navigate;
    private readonly Func<string, int, byte[]?>? _extractIcon;
    private readonly Dictionary<(string, int), byte[]?> _iconCache = new();

    public PackageListViewModel(Catalog catalog, ScanReport scan,
        Func<PackageEntry, IReadOnlyList<InstalledApp>> findInstalled,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> install,
        Func<PackageEntry, string, CancellationToken, Task<string>> extractDoc,
        Func<string, bool> confirm, Action<string> openFile, Action<object> navigate,
        Func<PackageEntry, IReadOnlyList<InstalledApp>, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> uninstall,
        Func<PackageEntry, IReadOnlyList<InstalledApp>, bool> canUninstall,
        Action? openAdmin = null, Func<string, int, byte[]?>? extractIcon = null)
    {
        CanOpenAdmin = openAdmin != null;
        OpenAdminCommand = new RelayCommand(() => openAdmin?.Invoke());
        _catalog = catalog; _scan = scan; _install = install; _uninstall = uninstall; _extractDoc = extractDoc; _confirm = confirm;
        _openFile = openFile; _navigate = navigate; _extractIcon = extractIcon;
        var byVersionDesc = Comparer<string>.Create((a, b) => VersionComparer.Compare(b, a) ?? string.CompareOrdinal(b, a));
        var sorted = catalog.Packages
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Version, byVersionDesc);
        foreach (var p in sorted) Rows.Add(new PackageRowViewModel(p, scan, findInstalled, canUninstall, this));
        Bundles = catalog.Bundles ?? new();
    }

    public bool CanOpenAdmin { get; }
    public IRelayCommand OpenAdminCommand { get; }

    public List<PackageRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string? _error;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand)), NotifyCanExecuteChangedFor(nameof(ViewScanCommand))]
    private bool _isInstalling;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(InstallSelectedLabel)), NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private int _selectedCount;

    public string InstallSelectedLabel => $"Cài các mục đã chọn ({SelectedCount})";

    partial void OnIsInstallingChanged(bool value)
    {
        ApplyBundleCommand.NotifyCanExecuteChanged();
        foreach (var r in Rows) r.NotifyInstallingChanged();
    }

    internal void OnSelectionChanged()
    {
        // A tick changed by hand drops the bundle's install order; the queue falls back to display order.
        if (!_applyingBundle) _bundleOrder = null;
        SelectedCount = Rows.Count(r => r.IsSelected);
    }

    // ---------- Bundles ----------

    /// <summary>The catalog's bundles; the picker is hidden when there are none.</summary>
    public IReadOnlyList<Bundle> Bundles { get; }
    public bool HasBundles => Bundles.Count > 0;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyBundleCommand))]
    private Bundle? _selectedBundle;

    /// <summary>One line per bundle item that was not ticked: "{name}: {reason}" when blocked, "{id}: Không có trong kho" when missing.</summary>
    public ObservableCollection<string> BundleNotices { get; } = new();
    public bool HasBundleNotices => BundleNotices.Count > 0;

    private List<PackageRowViewModel>? _bundleOrder;
    private bool _applyingBundle;

    private bool CanApplyBundle() => SelectedBundle != null && !IsInstalling;

    /// <summary>Clears the ticks, then ticks the bundle's resolvable and allowed packages, remembering the bundle's order.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyBundle))]
    private void ApplyBundle()
    {
        if (SelectedBundle is not { } bundle) return;
        var order = new List<PackageRowViewModel>();
        var notices = new List<string>();
        _applyingBundle = true;
        try
        {
            foreach (var r in Rows) r.IsSelected = false;
            foreach (var (item, pkg) in BundleResolver.Resolve(_catalog, bundle))
            {
                var row = pkg is null ? null : Rows.FirstOrDefault(r => ReferenceEquals(r.Package, pkg));
                if (row is null) { notices.Add($"{item.PackageId}: {Strings.NotInVault}"); continue; }
                if (!row.Allowed) { notices.Add($"{row.Name}: {row.ReasonsText}"); continue; }
                if (order.Contains(row)) continue; // "id" and "id@ver" can resolve to the same package
                row.IsSelected = true;
                order.Add(row);
            }
        }
        finally { _applyingBundle = false; }
        _bundleOrder = order;
        SetNotices(notices);
    }

    private void SetNotices(IEnumerable<string> notices)
    {
        BundleNotices.Clear();
        foreach (var n in notices) BundleNotices.Add(n);
        OnPropertyChanged(nameof(HasBundleNotices));
    }

    internal QueueInstallViewModel? Current { get; private set; }

    /// <summary>Tests replace this to inject a synchronous progress factory. Args: title, items, run, onBack, runningText.</summary>
    internal Func<string, IReadOnlyList<PackageEntry>, Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>>,
        Action, string, QueueInstallViewModel> CreateQueueVm { get; set; } =
        (t, items, run, back, running) => new QueueInstallViewModel(t, items, run, back, running);

    private bool CanInstallSelected() => SelectedCount > 0 && !IsInstalling;

    /// <summary>Installs the ticked rows: in bundle order right after a bundle was applied, otherwise in display order.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private void InstallSelected() =>
        StartInstall((_bundleOrder ?? Rows).Where(r => r.IsSelected && r.Allowed).ToList());

    internal void StartInstall(IReadOnlyList<PackageRowViewModel> rows)
    {
        if (IsInstalling || rows.Count == 0) return;

        // Downgrades are confirmed once, up front; a "no" drops those rows from the queue.
        var downgrades = rows.Where(r => r.NeedsDowngradeConfirm).ToList();
        if (downgrades.Count > 0 && !_confirm(DowngradeText(downgrades)))
            rows = rows.Except(downgrades).ToList();
        if (rows.Count == 0) return;

        RunQueue(InstallQueueTitle, rows.Select(r => r.Package).ToList(), (pkg, pr, ct) => _install(pkg, pr, ct), "Đang cài");
    }

    private static string DowngradeText(IReadOnlyList<PackageRowViewModel> rows)
    {
        if (rows.Count == 1)
        {
            var r = rows[0];
            return $"Máy đang có phiên bản mới hơn ({r.InstalledVersionRaw}). Vẫn cài {r.Name} {r.PackageVersion}?";
        }
        var lines = rows.Select(r => $"• {r.Name} {r.PackageVersion} (trên máy: {r.InstalledVersionRaw})");
        return "Máy đang có phiên bản mới hơn của các gói sau:\n" + string.Join("\n", lines) +
               "\n\nVẫn cài các gói này? Nếu không đồng ý, các gói trên sẽ được bỏ khỏi hàng đợi.";
    }

    internal void StartUninstall(PackageRowViewModel row)
    {
        if (IsInstalling || !row.CanUninstall) return;
        var pkg = row.Package;
        if (!_confirm($"Gỡ {pkg.Name} {row.InstalledVersionRaw ?? pkg.Version} khỏi máy?")) return;
        // Only this package's own matches are handed to the uninstaller.
        var matches = row.Matches;
        RunQueue(UninstallQueueTitle, new[] { pkg }, (p, pr, ct) => _uninstall(p, matches, pr, ct),
            QueueInstallViewModel.UninstallRunningText);
    }

    private void RunQueue(string title, IReadOnlyList<PackageEntry> items,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, string runningText)
    {
        Error = null;
        IsInstalling = true;
        var vm = CreateQueueVm(title, items, run, () =>
        {
            foreach (var r in Rows) { r.IsSelected = false; r.Refresh(); }
            _bundleOrder = null;
            SetNotices(Array.Empty<string>());
            IsInstalling = false;
            Current = null;
            _navigate(this);
        }, runningText);
        Current = vm;
        _navigate(vm);
    }

    private bool CanViewScan() => !IsInstalling;

    /// <summary>Re-opens this session's scan read-only; Back returns to this same instance, so ticks are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanViewScan))]
    private void ViewScan() =>
        _navigate(ScanReportViewModel.ForReport(_scan, ScanReportViewModel.DefaultTitle, () => _navigate(this)));

    internal byte[]? ExtractIconCached(string path, int index)
    {
        if (_extractIcon is null) return null;
        var key = (path.ToUpperInvariant(), index);
        lock (_iconCache)
        {
            if (_iconCache.TryGetValue(key, out var hit)) return hit;
        }
        byte[]? png;
        try { png = _extractIcon(path, index); }
        catch (Exception) { png = null; }
        lock (_iconCache) _iconCache[key] = png;
        return png;
    }

    internal async Task OpenDocAsync(PackageRowViewModel row, string doc)
    {
        try
        {
            Error = null;
            var path = await _extractDoc(row.Package, doc, CancellationToken.None);
            _openFile(path);
        }
        catch (Exception ex)
        {
            Error = $"Không mở được tài liệu: {ErrorText.ForUser(ex)}";
        }
    }
}
