using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.App.Converters;
using Installer.Core;
using Installer.Core.Model;
using Installer.Core.Platform;
using Installer.Core.Rules;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.ViewModels;

public partial class PackageRowViewModel : ObservableObject
{
    private readonly ScanReport _scan;
    private readonly Func<PackageEntry, IReadOnlyList<InstalledApp>> _findInstalled;
    private readonly Func<PackageEntry, IReadOnlyList<InstalledApp>, bool> _canUninstallFn;
    private readonly PackageListViewModel _owner;

    internal PackageRowViewModel(PackageEntry package, ScanReport scan,
        Func<PackageEntry, IReadOnlyList<InstalledApp>> findInstalled,
        Func<PackageEntry, IReadOnlyList<InstalledApp>, bool> canUninstall, PackageListViewModel owner)
    {
        Package = package; _scan = scan; _findInstalled = findInstalled; _canUninstallFn = canUninstall; _owner = owner;
        Icon = DecodeIcon(package.IconPngB64);
        Refresh();
    }

    public PackageEntry Package { get; }
    public string Name => Package.Name;
    public string PackageVersion => Package.Version;
    public IReadOnlyList<string> Docs => Package.Docs ?? new();
    public bool HasDocs => Docs.Count > 0;

    /// <summary>The package's own icon (PNG bytes); null when missing or not valid base64 (the view shows the default icon).</summary>
    public byte[]? Icon { get; }

    [ObservableProperty] private string _installedVersion = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsInstall)), NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private VersionStatus _status;

    /// <summary>False once the machine has this version or a newer one: the install button stays visible but greyed out.</summary>
    public bool NeedsInstall => Status is not (VersionStatus.Equal or VersionStatus.Newer);
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(InstallCommand))] private bool _allowed;
    [ObservableProperty] private string _reasonsText = "";
    [ObservableProperty] private bool _needsDowngradeConfirm;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UninstallToolTip)), NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private bool _canUninstall;

    /// <summary>Shown on the (disabled) Gỡ button when the package is installed but cannot be removed automatically.</summary>
    public string? UninstallToolTip => CanUninstall ? null : Strings.NoUninstall;

    internal string? InstalledVersionRaw { get; private set; }
    internal IReadOnlyList<InstalledApp> Matches { get; private set; } = Array.Empty<InstalledApp>();

    private bool _isSelected;

    /// <summary>Checkbox state. Blocked rows can never be selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var v = value && Allowed;
            if (SetProperty(ref _isSelected, v)) _owner.OnSelectionChanged();
            else if (v != value) OnPropertyChanged(); // push the rejected value back to the checkbox
        }
    }

    public void Refresh()
    {
        IReadOnlyList<InstalledApp> matches;
        try { matches = _findInstalled(Package) ?? Array.Empty<InstalledApp>(); }
        catch (Exception) { matches = Array.Empty<InstalledApp>(); }
        Matches = matches;

        var check = VersionComparer.Check(matches, Package.Version);
        var rule = RuleEvaluator.Evaluate(_scan, Package, check);
        InstalledVersionRaw = check.InstalledVersion;
        InstalledVersion = FormatInstalled(matches, Package.Version) ?? check.InstalledVersion ?? "—";
        Status = check.Status;
        StatusText = DisplayText.Of(check.Status);
        Allowed = rule.Allowed;
        ReasonsText = string.Join("; ", rule.Reasons);
        NeedsDowngradeConfirm = rule.NeedsDowngradeConfirm;
        IsInstalled = matches.Count > 0;
        bool can;
        try { can = IsInstalled && _canUninstallFn(Package, matches); }
        catch (Exception) { can = false; }
        CanUninstall = can;
        if (!Allowed && _isSelected) IsSelected = false;
    }

    /// <summary>
    /// "23.01 (x64), 22.01 (x86)" when more than one comparable version is installed (spec req. 12);
    /// null otherwise, so the caller falls back to the single best version.
    /// </summary>
    internal static string? FormatInstalled(IReadOnlyList<InstalledApp> matches, string packageVersion)
    {
        var comparable = matches
            .Where(a => a.DisplayVersion is not null && VersionComparer.Compare(a.DisplayVersion, packageVersion) is not null)
            .Select(a => (Version: a.DisplayVersion!, Arch: a.Is64Bit ? "x64" : "x86"))
            .Distinct()
            .ToList();
        if (comparable.Count < 2) return null;
        comparable.Sort((a, b) => VersionComparer.Compare(b.Version, a.Version) ?? 0);
        return string.Join(", ", comparable.Select(c => $"{c.Version} ({c.Arch})"));
    }

    internal static byte[]? DecodeIcon(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return null;
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }
    }

    internal void NotifyInstallingChanged()
    {
        InstallCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstall() => Allowed && NeedsInstall && !_owner.IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => _owner.StartInstall(new[] { this });

    private bool CanRunUninstall() => CanUninstall && !_owner.IsInstalling;

    [RelayCommand(CanExecute = nameof(CanRunUninstall))]
    private void Uninstall() => _owner.StartUninstall(this);

    [RelayCommand]
    private Task OpenDocAsync(string? doc) => doc is null ? Task.CompletedTask : _owner.OpenDocAsync(this, doc);
}
