using System.Diagnostics;

namespace Installer.Core.Install;

public sealed class ExeInstaller : IInstallerRunner
{
    public async Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (req.Timeout <= TimeSpan.Zero)
            return new InstallOutcome(InstallStatus.Failed, null, "Lỗi cài đặt (thời gian chờ không hợp lệ)");
        progress.Report(new InstallProgress(null, "Đang khởi động bộ cài"));
        var psi = new ProcessStartInfo(req.InstallerPath)
        {
            Arguments = req.SilentArgs ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(req.InstallerPath)) ?? "",
        };
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { return new InstallOutcome(InstallStatus.Failed, null, Errors.ErrorText.ForUser(ex)); }
        if (proc == null) return new InstallOutcome(InstallStatus.Failed, null, "Không khởi động được bộ cài");

        using (proc)
        {
            progress.Report(new InstallProgress(null, "Đang cài đặt"));
            using var timeoutCts = new CancellationTokenSource(req.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                try
                {
                    using var killWait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await proc.WaitForExitAsync(killWait.Token).ConfigureAwait(false);
                }
                catch { }
                return timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested
                    ? new InstallOutcome(InstallStatus.Timeout, null, "Quá thời gian cài đặt")
                    : new InstallOutcome(InstallStatus.Failed, null, "Người dùng hủy cài đặt");
            }
            var outcome = ExitCodeMapper.Map(proc.ExitCode);
            if (outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired)
                progress.Report(new InstallProgress(100, "Hoàn tất"));
            return outcome;
        }
    }
}
