using System.Text;
using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class ScriptRunnerTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"), "Thư mục có dấu");

    public ScriptRunnerTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_tmp)!, true); } catch { } }

    string Script(string name, string body)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllText(p, body, new UTF8Encoding(true));
        return p;
    }

    static Task<InstallOutcome> Run(string path, int timeoutSec = 60, CancellationToken ct = default) =>
        new ScriptRunner().RunAsync(new InstallRequest(InstallerType.Script, path, "", null, TimeSpan.FromSeconds(timeoutSec)),
            new Progress<InstallProgress>(), ct);

    [Fact]
    public async Task Cmd_exit_0_is_success()
    {
        var r = await Run(Script("ok.cmd", "@echo off\r\nexit /b 0\r\n"));
        Assert.Equal(InstallStatus.Success, r.Status);
    }

    [Fact]
    public async Task Cmd_exit_3_is_failed_with_code()
    {
        var r = await Run(Script("bad.cmd", "@echo off\r\nexit /b 3\r\n"));
        Assert.Equal(InstallStatus.Failed, r.Status);
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("Lỗi cài đặt (mã 3)", r.Message);
    }

    [Fact]
    public async Task Output_is_captured_and_limited_to_4000_chars()
    {
        var r = await Run(Script("out.cmd", "@echo off\r\necho hello-out\r\necho hello-err 1>&2\r\n"));
        Assert.Contains("hello-out", r.Detail);
        Assert.Contains("hello-err", r.Detail);

        var big = await Run(Script("big.cmd", "@echo off\r\nfor /l %%i in (1,1,2000) do echo line-%%i-xxxxxxxxxxxx\r\n"));
        Assert.Equal(InstallStatus.Success, big.Status);
        Assert.True(big.Detail!.Length <= ScriptRunner.MaxOutput);
        Assert.Contains("line-2000-", big.Detail);
    }

    [Fact]
    public async Task Timeout_kills_process_tree()
    {
        var r = await Run(Script("slow.cmd", "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n"), timeoutSec: 1);
        Assert.Equal(InstallStatus.Timeout, r.Status);
    }

    [Fact]
    public async Task Ps1_runs_and_writes_file_in_script_folder()
    {
        var p = Script("w.ps1", "Set-Content -LiteralPath (Join-Path (Get-Location) 'made.txt') -Value 'xin chào'\r\nWrite-Output 'ps-ok'\r\n");
        var r = await Run(p);
        Assert.Equal(InstallStatus.Success, r.Status);
        Assert.True(File.Exists(Path.Combine(_tmp, "made.txt")));
        Assert.Contains("ps-ok", r.Detail);
    }
}
