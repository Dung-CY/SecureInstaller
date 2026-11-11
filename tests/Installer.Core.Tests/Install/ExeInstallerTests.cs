using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class ExeInstallerTests : IDisposable
{
    private readonly string _top = Path.Combine(Path.GetTempPath(), "SI_t_" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public ExeInstallerTests()
    {
        _root = Path.Combine(_top, "Dự án 1", "setup x");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_top, true); } catch { } }

    private sealed class Collect : IProgress<InstallProgress>
    {
        public List<InstallProgress> Items = new();
        public void Report(InstallProgress v) => Items.Add(v);
    }

    private string Script(string name, string body)
    {
        var p = Path.Combine(_root, name);
        File.WriteAllText(p, "@echo off\r\n" + body + "\r\n");
        return p;
    }

    [Fact]
    public async Task Exe_passes_args_with_vietnamese_path()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "TestData", "fake-setup.cmd");
        var dst = Path.Combine(_root, "fake-setup.cmd");
        File.Copy(src, dst);
        var prog = new Collect();
        var o = await new ExeInstaller().RunAsync(
            new InstallRequest(InstallerType.Exe, dst, "/S /D=C:\\X", null, TimeSpan.FromSeconds(20)), prog, CancellationToken.None);
        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.Equal("/S /D=C:\\X", File.ReadAllText(Path.Combine(_root, "out.txt")).Trim());
        Assert.Equal(new InstallProgress(100, "Hoàn tất"), prog.Items[^1]);
        Assert.Equal("Đang khởi động bộ cài", prog.Items[0].Step);
    }

    [Fact]
    public async Task Exe_nonzero_exit_maps_failed()
    {
        var s = Script("exit1603.cmd", "exit /b 1603");
        var o = await new ExeInstaller().RunAsync(
            new InstallRequest(InstallerType.Exe, s, "", null, TimeSpan.FromSeconds(20)), new Collect(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal(1603, o.ExitCode);
        Assert.Equal("Lỗi cài đặt (mã 1603)", o.Message);
    }

    [Fact]
    public async Task Exe_timeout_kills_and_returns_Timeout()
    {
        var s = Script("sleep.cmd", "ping -n 30 127.0.0.1 >nul");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var o = await new ExeInstaller().RunAsync(
            new InstallRequest(InstallerType.Exe, s, "", null, TimeSpan.FromSeconds(1)), new Collect(), CancellationToken.None);
        Assert.Equal(InstallStatus.Timeout, o.Status);
        Assert.Equal("Quá thời gian cài đặt", o.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }

    [Fact]
    public async Task Exe_cancel_kills_and_returns_Failed()
    {
        var s = Script("sleep.cmd", "ping -n 30 127.0.0.1 >nul");
        using var cts = new CancellationTokenSource(500);
        var o = await new ExeInstaller().RunAsync(
            new InstallRequest(InstallerType.Exe, s, "", null, TimeSpan.FromSeconds(20)), new Collect(), cts.Token);
        Assert.Equal(InstallStatus.Failed, o.Status);
        Assert.Equal("Người dùng hủy cài đặt", o.Message);
    }

    [Fact]
    public async Task Exe_missing_file_fails()
    {
        var o = await new ExeInstaller().RunAsync(
            new InstallRequest(InstallerType.Exe, Path.Combine(_root, "nope.exe"), "", null, TimeSpan.FromSeconds(5)), new Collect(), CancellationToken.None);
        Assert.Equal(InstallStatus.Failed, o.Status);
    }

    [Fact]
    public void Factory_returns_runners()
    {
        var f = new InstallerRunnerFactory();
        Assert.IsType<MsiInstaller>(f.For(InstallerType.Msi));
        Assert.IsType<ExeInstaller>(f.For(InstallerType.Exe));
        Assert.IsType<ZipInstaller>(f.For(InstallerType.Zip));
    }
}
