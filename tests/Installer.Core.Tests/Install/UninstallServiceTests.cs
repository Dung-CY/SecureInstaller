using Installer.Core.Install;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Repository;
using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;
using Installer.Core.Versioning;
using Microsoft.Win32;

namespace Installer.Core.Tests.Install;

public class UninstallServiceTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_unin_" + Guid.NewGuid().ToString("N"));
    static readonly HardwareInfo Hw = new("HOST1", null, null, "BIOS1", new(), new(), new(), new());
    const string MarkerKey = @"SOFTWARE\SecureInstaller\Installed\p1";

    public UninstallServiceTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    sealed class Repo : IRepositorySource
    {
        public List<LogEntry> Logs { get; } = new();
        public Catalog Catalog => Catalog.Empty();
        public Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetPackageKeyAsync(PackageEntry pkg, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetDocsKeyAsync(PackageEntry pkg, CancellationToken ct) => throw new NotSupportedException();
        public void WriteLog(LogEntry entry) => Logs.Add(entry);
    }

    static PackageEntry Pkg(InstallerType t, string? target = null, UninstallSpec? un = null) =>
        new("p1", "App", "1.0", t, "a.exe", "", target, 60, new DetectRule(null, "^App$"), new(), new(), new(),
            "b", "s", "k", Uninstall: un);

    static InstalledApp App(bool perUser, string? quiet) =>
        new("App", "1.0", "AppKey", true, perUser, null, quiet);

    (UninstallService svc, Repo repo, FakeRunner runner, FakeRegistry reg) Make()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, MarkerKey, new() { ["Version"] = "1.0" });
        var repo = new Repo();
        var runner = new FakeRunner();
        return (new UninstallService(repo, new InstallMarker(reg, reg), _ => runner), repo, runner, reg);
    }

    static bool MarkerExists(FakeRegistry reg) =>
        reg.GetValue(RegistryHive.LocalMachine, RegistryView.Registry64, MarkerKey, "Version") != null;

    static readonly IProgress<InstallProgress> NoProgress = new Progress<InstallProgress>();

    [Fact]
    public async Task Zip_uninstall_deletes_target_and_marker()
    {
        var (svc, _, _, reg) = Make();
        var target = Path.Combine(_tmp, "Dự án 1", "App");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "x.txt"), "x");
        var pkg = Pkg(InstallerType.Zip, target);
        Assert.True(svc.CanUninstall(pkg, new List<InstalledApp>()));
        var o = await svc.UninstallAsync(pkg, new List<InstalledApp>(), "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.False(Directory.Exists(target));
        Assert.False(MarkerExists(reg));
    }

    [Fact]
    public async Task Command_from_hkcu_only_is_never_used()
    {
        var (svc, _, runner, reg) = Make();
        var pkg = Pkg(InstallerType.Exe);
        var installed = new List<InstalledApp> { App(true, "\"C:\\Evil\\u.exe\" /S") };
        Assert.False(svc.CanUninstall(pkg, installed));
        var o = await svc.UninstallAsync(pkg, installed, "u", Hw, NoProgress, CancellationToken.None);
        Assert.NotEqual(InstallStatus.Success, o.Status);
        Assert.Empty(runner.Requests);
        Assert.True(MarkerExists(reg));
    }

    [Fact]
    public async Task Hklm_quiet_uninstall_string_runs_through_runner()
    {
        var (svc, _, runner, reg) = Make();
        var pkg = Pkg(InstallerType.Exe);
        var installed = new List<InstalledApp> { App(true, "\"C:\\Evil\\u.exe\" /S"), App(false, "\"C:\\Program Files\\App\\un.exe\" /S /quiet") };
        Assert.True(svc.CanUninstall(pkg, installed));
        var o = await svc.UninstallAsync(pkg, installed, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("C:\\Program Files\\App\\un.exe", req.InstallerPath);
        Assert.Equal("/S /quiet", req.SilentArgs);
        Assert.Equal(TimeSpan.FromSeconds(60), req.Timeout);
        Assert.False(MarkerExists(reg));
    }

    [Theory]
    [InlineData("un.exe")]
    [InlineData("\\\\server\\share\\un.exe")]
    [InlineData("\\Windows\\un.exe")]
    public async Task Manifest_command_must_be_fully_qualified(string cmd)
    {
        var (svc, _, runner, reg) = Make();
        var pkg = Pkg(InstallerType.Exe, un: new UninstallSpec("command", cmd, "/S"));
        var o = await svc.UninstallAsync(pkg, new List<InstalledApp>(), "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Blocked, o.Status);
        Assert.Empty(runner.Requests);
        Assert.True(MarkerExists(reg));
    }

    static readonly List<InstalledApp> None = new();
    const string G = "{12345678-1234-1234-1234-123456789ABC}";

    [Theory]
    [InlineData("un.exe")]
    [InlineData("\\\\server\\share\\un.exe")]
    public void CanUninstall_false_for_invalid_manifest_command(string cmd)
    {
        var (svc, _, _, _) = Make();
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Exe, un: new UninstallSpec("command", cmd, "/S")), None));
    }

    [Theory]
    [InlineData("MsiExec.exe /X" + G)]
    [InlineData("\"C:\\Windows\\System32\\msiexec.exe\" /I" + G + " /qn")]
    public void Msiexec_quiet_string_resolves_to_msi_plan(string line)
    {
        var (svc, _, runner, _) = Make();
        Assert.True(svc.CanUninstall(Pkg(InstallerType.Exe), new List<InstalledApp> { App(false, line) }));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Msiexec_without_guid_cannot_uninstall()
    {
        var (svc, _, runner, _) = Make();
        var inst = new List<InstalledApp> { App(false, "MsiExec.exe /quiet") };
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Exe), inst));
        var o = await svc.UninstallAsync(Pkg(InstallerType.Exe), inst, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Blocked, o.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Zip_without_target_cannot_uninstall()
    {
        var (svc, _, _, _) = Make();
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Zip, null), None));
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Zip, "  "), None));
    }

    [Fact]
    public async Task Zip_with_protected_target_fails()
    {
        var (svc, _, _, reg) = Make();
        var o = await svc.UninstallAsync(Pkg(InstallerType.Zip, "C:\\"), None, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.True(MarkerExists(reg));
    }

    [Fact]
    public void PerUser_guid_and_marker_rows_ignored_for_msi()
    {
        var (svc, _, _, _) = Make();
        var inst = new List<InstalledApp>
        {
            new("App", "1.0", G, true, true),
            new("App", "1.0", G, true, false, IsMarker: true),
        };
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Msi), inst));
    }

    [Fact]
    public async Task Explicit_manifest_command_wins_over_installer_type()
    {
        var (svc, _, runner, _) = Make();
        var pkg = Pkg(InstallerType.Msi, un: new UninstallSpec("command", "C:\\Tools\\un.exe", "/S"));
        var inst = new List<InstalledApp> { new("App", "1.0", G, true, false) };
        var o = await svc.UninstallAsync(pkg, inst, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.Equal("C:\\Tools\\un.exe", Assert.Single(runner.Requests).InstallerPath);
    }

    [Fact]
    public void Explicit_msi_without_guid_does_not_fall_through_to_zip()
    {
        var (svc, _, _, _) = Make();
        var pkg = Pkg(InstallerType.Zip, Path.Combine(_tmp, "t"), new UninstallSpec("msi"));
        Assert.False(svc.CanUninstall(pkg, None));
    }

    [Fact]
    public void Malformed_command_cannot_uninstall_and_does_not_throw()
    {
        var (svc, _, _, _) = Make();
        var pkg = Pkg(InstallerType.Exe, un: new UninstallSpec("command", "C:\\a\0b\\un.exe", "/S"));
        Assert.False(svc.CanUninstall(pkg, None));
        Assert.False(svc.CanUninstall(Pkg(InstallerType.Exe), new List<InstalledApp> { App(false, "C:\\a\0b\\un.exe /S") }));
    }

    [Fact]
    public async Task Message_uses_NoUninstall()
    {
        var (svc, _, _, _) = Make();
        var o = await svc.UninstallAsync(Pkg(InstallerType.Exe), None, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Blocked, o.Status);
        Assert.Equal(Strings.NoUninstall, o.Message);
    }

    [Fact]
    public async Task Manifest_command_expands_env_and_runs()
    {
        var (svc, _, runner, _) = Make();
        var pkg = Pkg(InstallerType.Exe, un: new UninstallSpec("command", "%ProgramFiles%\\App\\un.exe", "/S"));
        var o = await svc.UninstallAsync(pkg, new List<InstalledApp>(), "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        var req = Assert.Single(runner.Requests);
        Assert.Equal(Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramFiles%"), "App", "un.exe"), req.InstallerPath);
        Assert.Equal("/S", req.SilentArgs);
    }

    [Fact]
    public async Task Failed_runner_keeps_marker()
    {
        var (svc, _, runner, reg) = Make();
        runner.Outcome = new InstallOutcome(InstallStatus.Failed, 1, "fail");
        var o = await svc.UninstallAsync(Pkg(InstallerType.Exe), new List<InstalledApp> { App(false, "C:\\a\\un.exe /S") }, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.True(MarkerExists(reg));
    }

    [Fact]
    public async Task Uninstall_logs_action_uninstall()
    {
        var (svc, repo, _, _) = Make();
        await svc.UninstallAsync(Pkg(InstallerType.Exe), new List<InstalledApp> { App(false, "C:\\a\\un.exe /S") }, "bob", Hw, NoProgress, CancellationToken.None);
        var l = Assert.Single(repo.Logs);
        Assert.Equal("uninstall", l.Action);
        Assert.Equal("p1", l.PackageId);
        Assert.Equal("bob", l.Username);
        Assert.Equal("Success", l.Status);
    }

    static PackageEntry KPkg(InstallerKind k) => Pkg(InstallerType.Exe) with { Kind = k };

    static InstalledApp Row(string uninstall, string key = "K") =>
        new("App", "1.0", key, true, false, uninstall);

    static UninstallService Mk(FakeRunner runner, Func<InstalledApp, bool>? exists = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var reg = new FakeRegistry();
        return new UninstallService(new Repo(), new InstallMarker(reg, reg), _ => runner, exists, delay);
    }

    [Theory]
    [InlineData(InstallerKind.Inno, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")]
    [InlineData(InstallerKind.Nsis, "/S")]
    [InlineData(InstallerKind.Burn, "/uninstall /quiet /norestart")]
    public async Task Derives_silent_arguments_from_the_uninstall_string(InstallerKind kind, string expected)
    {
        var runner = new FakeRunner();
        var rows = new List<InstalledApp> { Row("\"C:\\Program Files\\App\\u.exe\"") };
        var svc = Mk(runner);
        Assert.True(svc.CanUninstall(KPkg(kind), rows));
        await svc.UninstallAsync(KPkg(kind), rows, "u", Hw, NoProgress, CancellationToken.None);
        var req = Assert.Single(runner.Requests);
        Assert.Equal(@"C:\Program Files\App\u.exe", req.InstallerPath);
        Assert.Equal(expected, req.SilentArgs);
    }

    [Fact]
    public async Task Burn_does_not_duplicate_uninstall_and_unins000_is_inno_whatever_the_kind()
    {
        var runner = new FakeRunner();
        var svc = Mk(runner);
        await svc.UninstallAsync(KPkg(InstallerKind.Burn), new List<InstalledApp> { Row("\"C:\\a\\b.exe\" /uninstall") }, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal("/uninstall /quiet /norestart", runner.Requests[0].SilentArgs);
        await svc.UninstallAsync(KPkg(InstallerKind.Unknown), new List<InstalledApp> { Row("\"C:\\a\\unins000.exe\"") }, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", runner.Requests[1].SilentArgs);
    }

    [Fact]
    public void InstallShield_unknown_and_hkcu_have_no_derived_plan_but_msiexec_guid_does()
    {
        var svc = Mk(new FakeRunner());
        var row = new List<InstalledApp> { Row("\"C:\\a\\setup.exe\" -runfromtemp") };
        Assert.False(svc.CanUninstall(KPkg(InstallerKind.InstallShield), row));
        Assert.False(svc.CanUninstall(KPkg(InstallerKind.Unknown), row));
        Assert.False(svc.CanUninstall(KPkg(InstallerKind.Nsis), new List<InstalledApp> { row[0] with { PerUser = true } }));
        Assert.True(svc.CanUninstall(KPkg(InstallerKind.Unknown), new List<InstalledApp> { Row("MsiExec.exe /X" + G) }));
    }

    [Fact]
    public async Task Poll_times_out_when_the_key_stays_and_succeeds_when_it_disappears()
    {
        var rows = new List<InstalledApp> { Row("\"C:\\a\\u.exe\"") };
        var slept = new List<TimeSpan>();
        var stay = Mk(new FakeRunner(), _ => true, (t, _) => { slept.Add(t); return Task.CompletedTask; });
        var o = await stay.UninstallAsync(KPkg(InstallerKind.Nsis), rows, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal("Gỡ chưa hoàn tất", o.Message);
        Assert.Equal(30, slept.Count); // 60 s budget, 2 s steps

        var polls = 0;
        var gone = Mk(new FakeRunner(), _ => ++polls < 3, (_, _) => Task.CompletedTask);
        var ok = await gone.UninstallAsync(KPkg(InstallerKind.Nsis), rows, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Burn_bundle_row_is_uninstalled_and_the_poll_waits_only_for_it()
    {
        var runner = new FakeRunner();
        var bundle = new InstalledApp(".NET Desktop Runtime", "8.0.11", "{de6c}", false, false, null,
            "\"C:\\ProgramData\\Package Cache\\{de6c}\\runtime.exe\" /uninstall /quiet");
        var child = new InstalledApp(".NET Desktop Runtime", "8.0.11", "{5F1E}", true, false, "MsiExec.exe /X" + G, SystemComponent: true);
        var svc = Mk(runner, a => a.KeyName == "{5F1E}", (_, _) => Task.CompletedTask); // the child stays, as in real life
        var o = await svc.UninstallAsync(KPkg(InstallerKind.Burn), new List<InstalledApp> { child, bundle }, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("/uninstall /quiet", req.SilentArgs);
        Assert.EndsWith("runtime.exe", req.InstallerPath);
    }

    [Fact]
    public void Nsis_uninst_exe_of_a_preinstalled_app_resolves_through_the_scanner_fallback()
    {
        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Bit4id - Universal MW (x)",
            new() { ["DisplayName"] = "Bit4id - Universal MW 1.4.10.839", ["DisplayVersion"] = "1.4.10.839", ["UninstallString"] = "\"C:\\Program Files (x86)\\Bit4id\\uninst.exe\"" });
        var pkg = Pkg(InstallerType.Exe) with { Name = "Bit4id Universal MW", Version = "1.4.10.839", Kind = InstallerKind.Nsis, Detect = new DetectRule(null, "^Bit4id Universal MW") };
        var rows = new InstalledAppsScanner(reg).FindForPackage(pkg);
        Assert.True(Mk(new FakeRunner()).CanUninstall(pkg, rows));
    }

    sealed class BlobRepo(string root) : IRepositorySource
    {
        public Catalog Catalog => Catalog.Empty();
        public Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct) =>
            Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, "vault", "packages", pkg.BlobFile)));
        public Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> GetPackageKeyAsync(PackageEntry pkg, CancellationToken ct) => Task.FromResult(Convert.FromBase64String(pkg.KeyB64));
        public Task<byte[]> GetDocsKeyAsync(PackageEntry pkg, CancellationToken ct) => throw new NotSupportedException();
        public void WriteLog(LogEntry entry) { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Script_package_uninstall_runs_the_script_from_a_workspace_that_is_deleted(bool withScript)
    {
        var src = Path.Combine(_tmp, "src"); var vault = Path.Combine(_tmp, "vault-root"); var ran = Path.Combine(_tmp, "ran.txt");
        Directory.CreateDirectory(Path.Combine(src, "scripts"));
        File.WriteAllText(Path.Combine(src, "scripts", "main.cmd"), "@exit /b 0");
        File.WriteAllText(Path.Combine(src, "scripts", "un.cmd"), $"@echo done> \"{ran}\"\r\n@exit /b 0");
        File.WriteAllText(Path.Combine(src, "manifest.json"), "{ \"id\": \"p1\", \"name\": \"S\", \"version\": \"1.0\", \"installer_type\": \"script\", \"installer_file\": \"main.cmd\""
            + (withScript ? ", \"uninstall_script\": \"un.cmd\"" : "") + " }");
        var pkg = Installer.Core.Vault.PackageBuilder.Build(src, vault);
        Assert.Equal(withScript ? "un.cmd" : null, pkg.UninstallScript);

        var reg = new FakeRegistry();
        reg.AddKey(RegistryHive.LocalMachine, RegistryView.Registry64, MarkerKey, new() { ["Version"] = "1.0" });
        TempWorkspace? ws = null;
        var svc = new UninstallService(new BlobRepo(vault), new InstallMarker(reg, reg), _ => new FakeRunner(),
            workspaceFactory: () => ws = TempWorkspace.Create(Path.Combine(Path.GetTempPath(), "SItest_ws", "work")));
        Assert.True(svc.CanUninstall(pkg, None));
        var o = await svc.UninstallAsync(pkg, None, "u", Hw, NoProgress, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.Equal(withScript, File.Exists(ran));
        Assert.Equal(withScript, ws != null);
        if (ws != null) Assert.True(ws.Deleted);
        Assert.False(MarkerExists(reg));
    }

    [Fact]
    public async Task Zip_uninstall_deletes_the_recorded_folder_not_an_edited_target_dir()
    {
        var (svc, _, _, reg) = Make();
        var parent = Path.Combine(_tmp, "Tools");
        var installed = Path.Combine(parent, "A");
        var other = Path.Combine(parent, "Khac");
        Directory.CreateDirectory(installed);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "giu.txt"), "x");
        reg.SetString(RegistryHive.LocalMachine, RegistryView.Registry64, MarkerKey, "TargetDir", installed);

        var o = await svc.UninstallAsync(Pkg(InstallerType.Zip, parent), None, "u", Hw, NoProgress, CancellationToken.None);

        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.False(Directory.Exists(installed));
        Assert.True(File.Exists(Path.Combine(other, "giu.txt")));
    }
}
