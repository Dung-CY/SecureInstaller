using Installer.App.Converters;
using Installer.App.ViewModels;
using Installer.Core.Licensing;
using Installer.Core.Logging;
using Installer.Core.Platform;
using Installer.Core.Scanning;
using Installer.Core.Vault;

namespace Installer.App;

/// <summary>Creates the real (non-test) objects and wires navigation between ViewModels.</summary>
public sealed class AppServices
{
    public MainViewModel Main { get; } = new();

    private readonly string _root = AppContext.BaseDirectory;
    private EncryptedLog? _log;

    public AppServices()
    {
        Main.NavigateTo(new LoginViewModel(() => VaultStore.Open(_root), OnLoggedIn, TimeProvider.System));
    }

    private void OnLoggedIn(VaultSession session)
    {
        _log = new EncryptedLog(_root, session.Store.Meta.VaultId, session.VaultKey, session.Store.IsReadOnly);
        _log.SyncFallbackIntoVault();

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
            report => Main.NavigateTo(new PlaceholderViewModel("Danh sách gói sẽ có ở bước tiếp theo.", report)));
        Main.NavigateTo(vm);
        _ = vm.ScanCommand.ExecuteAsync(null);
    }

    private void LogScan(VaultSession session, ScanReport r)
    {
        _log?.Append(new LogEntry(DateTimeOffset.Now, session.Username, r.Hardware.Hostname, r.Hardware.BiosSerial,
            "scan", null, null, "ok", null,
            $"Windows: {DisplayText.Of(r.Windows.Verdict)}; Office: {DisplayText.Of(r.Office.Verdict)}; File từ khóa: {r.Keywords.MatchedFiles.Count}"));
    }
}
