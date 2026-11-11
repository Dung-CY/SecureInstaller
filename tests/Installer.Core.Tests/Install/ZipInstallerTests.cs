using System.IO.Compression;
using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class ZipInstallerTests : IDisposable
{
    private readonly string _top = Path.Combine(Path.GetTempPath(), "SI_z_" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public ZipInstallerTests()
    {
        _root = Path.Combine(_top, "Dự án 1");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_top, true); } catch { } }

    private sealed class Collect : IProgress<InstallProgress>
    {
        public List<InstallProgress> Items = new();
        public void Report(InstallProgress v) => Items.Add(v);
    }

    private string MakeZip(params string[] entries)
    {
        var z = Path.Combine(_root, "a.zip");
        using var fs = File.Create(z);
        using var za = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            var en = za.CreateEntry(e);
            if (!e.EndsWith('/'))
                using (var w = new StreamWriter(en.Open())) w.Write("data " + e);
        }
        return z;
    }

    [Fact]
    public async Task Zip_extracts_and_reports_100()
    {
        var z = MakeZip("a.txt", "sub/", "sub/b.txt");
        var target = Path.Combine(_root, "đích x");
        var prog = new Collect();
        var o = await new ZipInstaller().RunAsync(new InstallRequest(InstallerType.Zip, z, "", target, TimeSpan.FromMinutes(1)), prog, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.Equal("data sub/b.txt", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
        Assert.Equal(new InstallProgress(100, "Hoàn tất"), prog.Items[^1]);
        Assert.Contains(prog.Items, i => i.Step == "Đang giải nén");
    }

    [Fact]
    public async Task Zip_slip_entry_fails_before_extracting()
    {
        var z = MakeZip("ok.txt", "..\\evil.txt");
        var target = Path.Combine(_root, "t");
        var o = await new ZipInstaller().RunAsync(new InstallRequest(InstallerType.Zip, z, "", target, TimeSpan.FromMinutes(1)), new Collect(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal("Gói zip không hợp lệ", o.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(target, "ok.txt")));
    }

    [Fact]
    public async Task Zip_precancelled_returns_failed_cancel()
    {
        var z = MakeZip("a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var o = await new ZipInstaller().RunAsync(new InstallRequest(InstallerType.Zip, z, "", Path.Combine(_root, "t"), TimeSpan.FromMinutes(1)), new Collect(), cts.Token);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal("Người dùng hủy cài đặt", o.Message);
    }

    [Fact]
    public async Task Zip_entry_with_colon_fails()
    {
        var z = MakeZip("a.txt:evil");
        var target = Path.Combine(_root, "t");
        var o = await new ZipInstaller().RunAsync(new InstallRequest(InstallerType.Zip, z, "", target, TimeSpan.FromMinutes(1)), new Collect(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal("Gói zip không hợp lệ", o.Message);
    }

    [Fact]
    public async Task Zip_missing_target_fails()
    {
        var z = MakeZip("a.txt");
        var o = await new ZipInstaller().RunAsync(new InstallRequest(InstallerType.Zip, z, "", null, TimeSpan.FromMinutes(1)), new Collect(), CancellationToken.None);
        Assert.Equal("Thiếu thư mục đích", o.Message);
    }
}
