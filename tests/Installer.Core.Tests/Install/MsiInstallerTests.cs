using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class MsiInstallerTests : IDisposable
{
    readonly string _logs = Path.Combine(Path.GetTempPath(), "SI_msilog " + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_logs, true); } catch { } }

    static readonly IProgress<InstallProgress> NoProgress = new Progress<InstallProgress>();
    static InstallRequest Req => new(InstallerType.Msi, @"C:\Dự án 1\app.msi", "ALLUSERS=1", null, TimeSpan.FromMinutes(1));

    [Fact]
    public async Task Error_1603_retries_once_with_msiexec_qn_norestart()
    {
        InstallRequest? seen = null;
        var msi = new MsiInstaller
        {
            LogDirectory = _logs,
            InstallProduct = (_, _) => 1603,
            RunMsiexec = (r, _, _) => { seen = r; return Task.FromResult(new InstallOutcome(InstallStatus.Success, 0, "ok")); },
        };

        var o = await msi.RunAsync(Req, NoProgress, CancellationToken.None);

        Assert.Equal(InstallStatus.Success, o.Status);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"), seen!.InstallerPath);
        Assert.StartsWith("/i \"C:\\Dự án 1\\app.msi\" /qn /norestart REBOOT=ReallySuppress ALLUSERS=1", seen.SilentArgs);
        Assert.Contains("/L*v+ \"" + _logs, seen.SilentArgs);
        Assert.Empty(Directory.GetFiles(_logs)); // success: the log is removed
    }

    [Fact]
    public async Task Other_failures_do_not_retry()
    {
        var retried = false;
        var msi = new MsiInstaller
        {
            LogDirectory = _logs,
            InstallProduct = (_, _) => 1618,
            RunMsiexec = (_, _, _) => { retried = true; return Task.FromResult(new InstallOutcome(InstallStatus.Success, 0, "ok")); },
        };

        var o = await msi.RunAsync(Req, NoProgress, CancellationToken.None);

        Assert.False(retried);
        Assert.Equal(1618, o.ExitCode);
    }
}
