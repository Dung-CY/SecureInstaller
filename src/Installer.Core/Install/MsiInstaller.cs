using System.Text.RegularExpressions;

namespace Installer.Core.Install;

/// <summary>Pure parser for MSI INSTALLMESSAGE_PROGRESS strings ("1: a 2: b 3: c 4: d").</summary>
internal sealed class MsiProgressParser
{
    private static readonly Regex Fields = new(@"(\d+):\s*(-?\d+)", RegexOptions.Compiled);
    private long _total, _position;
    private bool _forward = true;
    private int _last;

    /// <summary>Returns a new percent only when it increased, otherwise null.</summary>
    public int? Handle(string progressMessage)
    {
        if (string.IsNullOrEmpty(progressMessage)) return null;
        var f = new Dictionary<int, long>();
        foreach (Match m in Fields.Matches(progressMessage))
            if (int.TryParse(m.Groups[1].Value, out int k) && long.TryParse(m.Groups[2].Value, out long v) && k is >= 1 and <= 4)
                f[k] = v;
        if (!f.TryGetValue(1, out long type)) return null;
        long b = f.GetValueOrDefault(2), c = f.GetValueOrDefault(3);
        switch (type)
        {
            case 0:
                _total = b;
                _last = 0; // each MSI phase reports from 0 again
                _forward = c == 0;
                _position = _forward ? 0 : _total;
                break;
            case 1:
                return null; // action info: step size only matters for tick accounting
            case 2:
                _position += _forward ? b : -b;
                break;
            case 3:
                _total += b;
                break;
            default:
                return null;
        }
        if (_total <= 0) return null;
        int pct = (int)Math.Clamp(_position * 100 / _total, 0, 100);
        if (pct > _last) { _last = pct; return pct; }
        return null;
    }
}

public sealed class MsiInstaller : IInstallerRunner
{
    // The external UI handler is process-global: serialize all MSI installs.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Regex ActionName = new(@"Action \d+:\d+:\d+:\s*([^.]+)\.", RegexOptions.Compiled);

    public async Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        try { await Gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new InstallOutcome(InstallStatus.Failed, null, "Người dùng hủy cài đặt"); }
        try
        {
            using var timeoutCts = new CancellationTokenSource(req.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            progress.Report(new InstallProgress(null, "Đang khởi động bộ cài"));
            var token = linked.Token;
            // Always wait for the install thread: the caller deletes the workspace afterwards.
            uint code = await Task.Factory.StartNew(() => Install(req, progress, token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);

            if (code == 1602)
                return timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested
                    ? new InstallOutcome(InstallStatus.Timeout, null, "Quá thời gian cài đặt")
                    : new InstallOutcome(InstallStatus.Failed, 1602, "Người dùng hủy cài đặt");
            var outcome = ExitCodeMapper.Map((int)code);
            if (outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired)
                progress.Report(new InstallProgress(100, "Hoàn tất"));
            return outcome;
        }
        catch (Exception ex)
        {
            return new InstallOutcome(InstallStatus.Failed, null, ex.Message);
        }
        finally { Gate.Release(); }
    }

    private static uint Install(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var parser = new MsiProgressParser();
        MsiNative.ExternalUIHandler handler = (_, msgType, msg) =>
        {
            try
            {
                if (token.IsCancellationRequested) return 2; // IDCANCEL -> MSI rolls back
                uint kind = msgType & 0xFF000000;
                if (kind == MsiNative.MSG_PROGRESS)
                {
                    int? p = parser.Handle(msg);
                    if (p != null) progress.Report(new InstallProgress(p, "Đang cài đặt"));
                }
                else if (kind == MsiNative.MSG_ACTIONSTART)
                {
                    var m = ActionName.Match(msg ?? "");
                    progress.Report(new InstallProgress(null, m.Success ? m.Groups[1].Value.Trim() : "Đang cài đặt"));
                }
            }
            catch { }
            return 0;
        };

        int prevUi = MsiNative.MsiSetInternalUI(MsiNative.INSTALLUILEVEL_NONE, IntPtr.Zero);
        IntPtr prevHandler = MsiNative.MsiSetExternalUI(handler,
            MsiNative.INSTALLLOGMODE_PROGRESS | MsiNative.INSTALLLOGMODE_ACTIONSTART, IntPtr.Zero);
        try
        {
            var cmd = ("ACTION=INSTALL REBOOT=ReallySuppress " + req.SilentArgs).Trim();
            return MsiNative.MsiInstallProductW(req.InstallerPath, cmd);
        }
        finally
        {
            MsiNative.MsiRestoreExternalUI(prevHandler, prevHandler == IntPtr.Zero ? 0u : 0xFFFFFFFFu, IntPtr.Zero);
            MsiNative.MsiSetInternalUI(prevUi, IntPtr.Zero);
            GC.KeepAlive(handler); // delegate must outlive the native registration
        }
    }
}
