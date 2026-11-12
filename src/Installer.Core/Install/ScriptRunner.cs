using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Installer.Core.Install;

/// <summary>Runs .ps1/.cmd/.bat with absolute interpreter paths (never PATH); keeps the last 4000 chars of output as Detail.</summary>
public sealed class ScriptRunner : IInstallerRunner
{
    public const int MaxOutput = 4000;
    public static readonly string[] Extensions = { ".ps1", ".cmd", ".bat" };

    public async Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (req.Timeout <= TimeSpan.Zero)
            return new InstallOutcome(InstallStatus.Failed, null, "Lỗi cài đặt (thời gian chờ không hợp lệ)");
        var script = Path.GetFullPath(req.InstallerPath);
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var ps = string.Equals(Path.GetExtension(script), ".ps1", StringComparison.OrdinalIgnoreCase);
        var enc = ps ? new UTF8Encoding(false) : OemEncoding();
        var psi = new ProcessStartInfo(ps ? Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe") : Path.Combine(sys, "cmd.exe"))
        {
            Arguments = ps ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"" : $"/c \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = enc,
            StandardErrorEncoding = enc,
            WorkingDirectory = Path.GetDirectoryName(script) ?? "",
        };
        progress.Report(new InstallProgress(null, "Đang chạy script"));
        var sb = new StringBuilder();
        void Add(string? line)
        {
            if (line == null) return;
            lock (sb)
            {
                sb.AppendLine(line);
                if (sb.Length > MaxOutput * 2) sb.Remove(0, sb.Length - MaxOutput);
            }
        }
        string Tail() { lock (sb) { var s = sb.ToString(); return s.Length > MaxOutput ? s[^MaxOutput..] : s; } }

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { return new InstallOutcome(InstallStatus.Failed, null, Errors.ErrorText.ForUser(ex), ex.ToString()); }
        if (proc == null) return new InstallOutcome(InstallStatus.Failed, null, "Không khởi động được script");

        using (proc)
        {
            proc.OutputDataReceived += (_, e) => Add(e.Data);
            proc.ErrorDataReceived += (_, e) => Add(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var timeoutCts = new CancellationTokenSource(req.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try { await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                try
                {
                    using var killWait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await proc.WaitForExitAsync(killWait.Token).ConfigureAwait(false);
                }
                catch { }
                var detail = Tail();
                return timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested
                    ? new InstallOutcome(InstallStatus.Timeout, null, "Quá thời gian cài đặt", detail)
                    : new InstallOutcome(InstallStatus.Failed, null, Errors.ErrorText.Cancelled, detail);
            }
            await Task.Run(() => proc.WaitForExit()).ConfigureAwait(false); // flush async output readers
            var code = proc.ExitCode;
            var outText = Tail();
            return code == 0
                ? new InstallOutcome(InstallStatus.Success, 0, "Cài đặt thành công", outText)
                : new InstallOutcome(InstallStatus.Failed, code, $"Lỗi cài đặt (mã {code})", outText);
        }
    }

    static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return new UTF8Encoding(false); }
    }
}
