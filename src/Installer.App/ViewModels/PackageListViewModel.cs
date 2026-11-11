using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Install;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.ViewModels;

public partial class PackageListViewModel : ObservableObject
{
    private readonly Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> _install;
    private readonly Func<PackageEntry, string, CancellationToken, Task<string>> _extractDoc;
    private readonly Func<string, bool> _confirm;
    private readonly Action<string> _openFile;
    private readonly Action<object> _navigate;

    public PackageListViewModel(Catalog catalog, ScanReport scan,
        Func<DetectRule, IReadOnlyList<InstalledApp>> findInstalled,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> install,
        Func<PackageEntry, string, CancellationToken, Task<string>> extractDoc,
        Func<string, bool> confirm, Action<string> openFile, Action<object> navigate, Action? openAdmin = null)
    {
        CanOpenAdmin = openAdmin != null;
        OpenAdminCommand = new RelayCommand(() => openAdmin?.Invoke());
        _install = install; _extractDoc = extractDoc; _confirm = confirm; _openFile = openFile; _navigate = navigate;
        var byVersionDesc = Comparer<string>.Create((a, b) => VersionComparer.Compare(b, a) ?? string.CompareOrdinal(b, a));
        var sorted = catalog.Packages
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Version, byVersionDesc);
        foreach (var p in sorted) Rows.Add(new PackageRowViewModel(p, scan, findInstalled, this));
    }

    public bool CanOpenAdmin { get; }
    public IRelayCommand OpenAdminCommand { get; }

    public List<PackageRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isInstalling;

    partial void OnIsInstallingChanged(bool value)
    {
        foreach (var r in Rows) r.NotifyInstallingChanged();
    }

    internal InstallViewModel? Current { get; private set; }

    internal void StartInstall(PackageRowViewModel row)
    {
        if (IsInstalling) return;
        var pkg = row.Package;
        if (row.NeedsDowngradeConfirm &&
            !_confirm($"Máy đang có phiên bản mới hơn ({row.InstalledVersionRaw}). Vẫn cài {pkg.Name} {pkg.Version}?"))
            return;

        Error = null;
        IsInstalling = true;
        var vm = CreateInstallVm($"{pkg.Name} {pkg.Version}", (progress, ct) => _install(pkg, progress, ct), () =>
        {
            row.Refresh();
            IsInstalling = false;
            _navigate(this);
        });
        Current = vm;
        _navigate(vm);
    }

    /// <summary>Tests replace this to inject a synchronous progress factory.</summary>
    internal Func<string, Func<IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>>, Action, InstallViewModel>
        CreateInstallVm { get; set; } = (t, run, back) => new InstallViewModel(t, run, back);

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
            Error = $"Không mở được tài liệu: {ex.Message}";
        }
    }
}
