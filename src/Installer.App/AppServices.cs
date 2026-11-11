using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using Installer.App.Converters;
using Installer.Core.Install;
using Installer.Core.Repository;
using Installer.App.ViewModels;
using Installer.Core.Licensing;
using Installer.Core.Logging;
using Installer.Core.Platform;
using Installer.Core.Scanning;
using Installer.Core.Vault;
using Installer.Core.Versioning;

namespace Installer.App;

/// <summary>Creates the real (non-test) objects and wires navigation between ViewModels.</summary>
public sealed class AppServices
{
    public MainViewModel Main { get; } = new();

    private readonly string _root = AppContext.BaseDirectory;
    private EncryptedLog? _log;
    private IRepositorySource? _repo;

    public AppServices()
    {
        Main.NavigateTo(new LoginViewModel(() => VaultStore.Open(_root), OnLoggedIn, TimeProvider.System));
    }

    private void OnLoggedIn(VaultSession session)
    {
        _log = new EncryptedLog(_root, session.Store.Meta.VaultId, session.VaultKey, session.Store.IsReadOnly);
        _log.SyncFallbackIntoVault();
        _repo = new PortableRepositorySource(session, _log);
        Main.SetVault(session.Catalog, session.Store.IsReadOnly);

        var wmi = new Wmi();
        var reg = new RegistryReader();
        var sys = new SystemInfo();
        var scanner = new MachineScanner(new HardwareScanner(wmi, sys),
            new NetworkHistoryScanner(reg, new ConnectivityProbe()),
            new LicenseScanner(wmi, reg, sys), new KeywordFileScanner());

        var vm = new ScanReportViewModel(
            async (progress, ct) =>
            {
                var report = await scanner.ScanAsync(session.Catalog, progress, ct);
                LogScan(session, report);
                return report;
            },
            report => ShowPackages(session, report));
        Main.NavigateTo(vm);
        _ = vm.ScanCommand.ExecuteAsync(null);
    }

    private void ShowPackages(VaultSession session, ScanReport report)
    {
        Main.SetVault(session.Catalog, session.Store.IsReadOnly);
        var installService = new InstallService(_repo!, new InstallerRunnerFactory());
        var apps = new InstalledAppsScanner(new RegistryReader());
        Main.NavigateTo(new PackageListViewModel(session.Catalog, report,
            rule => apps.Find(rule),
            (pkg, progress, ct) => installService.InstallAsync(pkg, session.Username, report.Hardware, progress, ct),
            (pkg, doc, ct) => installService.ExtractDocAsync(pkg, doc, ct),
            ConfirmYesNo,
            path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            Main.NavigateTo,
            session.IsAdmin ? () => ShowAdmin(session, report) : null));
    }

    private void ShowAdmin(VaultSession session, ScanReport report)
    {
        // Back rebuilds the package list from the current catalog, which the admin may have changed.
        Main.NavigateTo(new AdminViewModel(session, Path.Combine(_root, "logs"), EncryptedLog.DefaultFallbackDir(),
            PickFolder, PickSavePath, ConfirmYesNo, () => ShowPackages(session, report)));
    }

    private static string? PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Chọn thư mục gói phần mềm" };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    private static string? PickSavePath()
    {
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "log.csv" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static bool ConfirmYesNo(string msg) =>
        MessageBox.Show(msg, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void LogScan(VaultSession session, ScanReport r)
    {
        _repo?.WriteLog(new LogEntry(DateTimeOffset.Now, session.Username, r.Hardware.Hostname, r.Hardware.BiosSerial,
            "scan", null, null, "ok", null,
            $"Windows: {DisplayText.Of(r.Windows.Verdict)}; Office: {DisplayText.Of(r.Office.Verdict)}; File từ khóa: {r.Keywords.MatchedFiles.Count}"));
    }
}
