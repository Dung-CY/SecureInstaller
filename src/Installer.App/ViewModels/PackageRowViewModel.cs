using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.App.Converters;
using Installer.Core.Model;
using Installer.Core.Rules;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.ViewModels;

public partial class PackageRowViewModel : ObservableObject
{
    private readonly ScanReport _scan;
    private readonly Func<DetectRule, IReadOnlyList<InstalledApp>> _findInstalled;
    private readonly PackageListViewModel _owner;

    internal PackageRowViewModel(PackageEntry package, ScanReport scan,
        Func<DetectRule, IReadOnlyList<InstalledApp>> findInstalled, PackageListViewModel owner)
    {
        Package = package; _scan = scan; _findInstalled = findInstalled; _owner = owner;
        Refresh();
    }

    public PackageEntry Package { get; }
    public string Name => Package.Name;
    public string PackageVersion => Package.Version;
    public IReadOnlyList<string> Docs => Package.Docs ?? new();
    public bool HasDocs => Docs.Count > 0;

    [ObservableProperty] private string _installedVersion = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(InstallCommand))] private bool _allowed;
    [ObservableProperty] private string _reasonsText = "";
    [ObservableProperty] private bool _needsDowngradeConfirm;

    internal string? InstalledVersionRaw { get; private set; }

    public void Refresh()
    {
        var check = VersionComparer.Check(_findInstalled(Package.Detect), Package.Version);
        var rule = RuleEvaluator.Evaluate(_scan, Package, check);
        InstalledVersionRaw = check.InstalledVersion;
        InstalledVersion = check.InstalledVersion ?? "—";
        StatusText = DisplayText.Of(check.Status);
        Allowed = rule.Allowed;
        ReasonsText = string.Join("; ", rule.Reasons);
        NeedsDowngradeConfirm = rule.NeedsDowngradeConfirm;
    }

    internal void NotifyInstallingChanged() => InstallCommand.NotifyCanExecuteChanged();

    private bool CanInstall() => Allowed && !_owner.IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => _owner.StartInstall(this);

    [RelayCommand]
    private Task OpenDocAsync(string? doc) => doc is null ? Task.CompletedTask : _owner.OpenDocAsync(this, doc);
}
