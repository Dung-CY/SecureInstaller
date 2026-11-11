using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Installer.Core;
using Installer.Core.Install;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Repository;
using Installer.Core.Scanning;
using Installer.Core.Tests.Fakes;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Install;

public class InstallServiceTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    static readonly HardwareInfo Hw = new("HOST1", null, null, "BIOS1", new(), new(), new(), new());

    public InstallServiceTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    sealed class Env
    {
        public string Root = "";
        public VaultSession Session = null!;
        public EncryptedLog Log = null!;
    }

    Env NewVault()
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        var s = store.Unlock("admin", "pw-1", T0);
        var fb = Path.Combine(_tmp, "fb");
        return new Env { Root = root, Session = s, Log = new EncryptedLog(root, store.Meta.VaultId, s.VaultKey, false, fb) };
    }

    static List<LogEntry> ReadLogs(Env e) =>
        EncryptedLog.ReadAll(Path.Combine(e.Root, "logs"), e.Session.Store.Meta.VaultId, e.Session.VaultKey).ToList();

    sealed class Tracker { public TempWorkspace? Ws; }

    static InstallService Svc(IRepositorySource repo, FakeRunner r, Tracker t) =>
        new(repo, new InstallerRunnerFactory(_ => r), () => t.Ws = TempWorkspace.Create());

    static string CopyAction(string src, string dst) =>
        "{\"type\":\"copy\",\"src\":" + JsonSerializer.Serialize(src) + ",\"dst\":" + JsonSerializer.Serialize(dst) + "}";

    sealed class Wrap : IRepositorySource
    {
        public Catalog Catalog { get; init; } = null!;
        public byte[] Blob = Array.Empty<byte>();
        public Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Blob));
        public Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct) => throw new NotSupportedException();
        public void WriteLog(LogEntry entry) { }
    }

    [Fact]
    public async Task Install_zip_package_success_runs_post_actions_logs_and_cleans_workspace()
    {
        var e = NewVault();
        var dst = Path.Combine(_tmp, "out", "a.ini");
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.CreateZip(_tmp, Path.Combine(_tmp, "target"), "[" + CopyAction("a.ini", dst) + "]"));
        var runner = new FakeRunner();
        var t = new Tracker();
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), runner, t)
            .InstallAsync(pkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);

        Assert.Equal(InstallStatus.Success, res.Status);
        var req = Assert.Single(runner.Requests);
        Assert.True(runner.InstallerExistedAtCall);
        Assert.EndsWith(Path.Combine("pkg", "installer", "app.zip"), req.InstallerPath);
        Assert.Equal(InstallerType.Zip, req.Type);
        Assert.Equal(TimeSpan.FromSeconds(1800), req.Timeout);
        Assert.True(File.Exists(dst));
        Assert.True(t.Ws!.Deleted);
        Assert.False(Directory.Exists(t.Ws.Path));
        var log = Assert.Single(ReadLogs(e));
        Assert.Equal("install", log.Action);
        Assert.Equal("Success", log.Status);
        Assert.Equal("admin", log.Username);
        Assert.Equal("HOST1", log.Hostname);
        Assert.Equal(pkg.Id, log.PackageId);
    }

    [Fact]
    public async Task Hash_mismatch_returns_PackageCorrupt_and_runner_not_called()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.Create(_tmp));
        var runner = new FakeRunner();
        var t = new Tracker();
        var bad = pkg with { Sha256 = new string('0', 64) };
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), runner, t)
            .InstallAsync(bad, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, res.Status);
        Assert.Equal(Strings.PackageCorrupt, res.Message);
        Assert.Empty(runner.Requests);
        Assert.True(t.Ws!.Deleted);
    }

    [Fact]
    public async Task Truncated_blob_returns_PackageCorrupt()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.Create(_tmp));
        var all = File.ReadAllBytes(Path.Combine(e.Root, "vault", "packages", pkg.BlobFile));
        var cut = all[..(all.Length - 20)];
        var fixedPkg = pkg with { Sha256 = Convert.ToHexString(SHA256.HashData(cut)).ToLowerInvariant() };
        var runner = new FakeRunner();
        var t = new Tracker();
        var res = await Svc(new Wrap { Catalog = e.Session.Catalog, Blob = cut }, runner, t)
            .InstallAsync(fixedPkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, res.Status);
        Assert.Equal(Strings.PackageCorrupt, res.Message);
        Assert.Empty(runner.Requests);
        Assert.True(t.Ws!.Deleted);
    }

    [Fact]
    public async Task Failed_install_skips_post_actions_and_logs_failed()
    {
        var e = NewVault();
        var dst = Path.Combine(_tmp, "out2", "a.ini");
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.CreateZip(_tmp, Path.Combine(_tmp, "t2"), "[" + CopyAction("a.ini", dst) + "]"));
        var runner = new FakeRunner { Outcome = new InstallOutcome(InstallStatus.Failed, 5, "boom") };
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), runner, new Tracker())
            .InstallAsync(pkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, res.Status);
        Assert.False(File.Exists(dst));
        var log = Assert.Single(ReadLogs(e));
        Assert.Equal("Failed", log.Status);
        Assert.Equal(5, log.ExitCode);
    }

    [Fact]
    public async Task Post_action_failure_appends_to_message_without_changing_status()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.CreateZip(_tmp, Path.Combine(_tmp, "t3"),
            "[" + CopyAction("missing.ini", Path.Combine(_tmp, "o3", "m.ini")) + "]"));
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), new FakeRunner(), new Tracker())
            .InstallAsync(pkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(InstallStatus.Success, res.Status);
        Assert.Contains("(1 thao tác sau cài lỗi)", res.Message);
        Assert.Contains("m.ini", Assert.Single(ReadLogs(e)).Detail);
    }

    [Fact]
    public async Task Runner_throws_workspace_still_deleted()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.Create(_tmp));
        var runner = new FakeRunner { Throw = new InvalidOperationException("kaboom") };
        var t = new Tracker();
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), runner, t)
            .InstallAsync(pkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, res.Status);
        Assert.Equal("kaboom", res.Message);
        Assert.True(t.Ws!.Deleted);
        Assert.Equal("Failed", Assert.Single(ReadLogs(e)).Status);
    }

    [Fact]
    public async Task Cancellation_returns_failed_and_deletes_workspace()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.Create(_tmp));
        var runner = new FakeRunner { Throw = new OperationCanceledException() };
        var t = new Tracker();
        var res = await Svc(new PortableRepositorySource(e.Session, e.Log), runner, t)
            .InstallAsync(pkg, "admin", Hw, new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal("Người dùng hủy cài đặt", res.Message);
        Assert.True(t.Ws!.Deleted);
    }

    [Fact]
    public async Task ExtractDoc_returns_readable_file()
    {
        var e = NewVault();
        var pkg = new VaultAdmin(e.Session).AddPackage(SamplePackage.Create(_tmp));
        var svc = Svc(new PortableRepositorySource(e.Session, e.Log), new FakeRunner(), new Tracker());
        var path = await svc.ExtractDocAsync(pkg, "HuongDan.docx", CancellationToken.None);
        try
        {
            Assert.Equal("huong dan", File.ReadAllText(path, Encoding.UTF8));
            Assert.Equal("HuongDan.docx", Path.GetFileName(path));
            await Assert.ThrowsAsync<ArgumentException>(() => svc.ExtractDocAsync(pkg, "..\\x", CancellationToken.None));
        }
        finally { InstallService.CleanupDocs(); }
    }
}
