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
}
