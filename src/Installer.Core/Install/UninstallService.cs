using System.Text.RegularExpressions;
using Installer.Core.Errors;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Platform;
using Installer.Core.Repository;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.Core.Install;

public sealed class UninstallService(IRepositorySource repo, InstallMarker marker, Func<InstallerType, IInstallerRunner> exeRunner)
{
    private static readonly Regex GuidRx = new(@"^\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}\z", RegexOptions.Compiled);

    private abstract record Plan;
    private sealed record ZipPlan(string Dir) : Plan;
    private sealed record MsiPlan(string ProductCode) : Plan;
    private sealed record CmdPlan(string Command, string Args) : Plan;

    public bool CanUninstall(PackageEntry pkg, IReadOnlyList<InstalledApp> installed) => Resolve(pkg, installed) != null;

    // Only machine-wide (HKLM) data may supply a command or ProductCode; HKCU is never trusted.
    private static Plan? Resolve(PackageEntry pkg, IReadOnlyList<InstalledApp> installed)
    {
        var type = pkg.Uninstall?.Type;
        if (string.Equals(type, "zip", StringComparison.OrdinalIgnoreCase) || pkg.InstallerType == InstallerType.Zip)
            return new ZipPlan(pkg.TargetDir ?? "");

        if (string.Equals(type, "msi", StringComparison.OrdinalIgnoreCase) || pkg.InstallerType == InstallerType.Msi)
        {
            var code = pkg.Detect?.ProductCode?.Trim();
            if (string.IsNullOrEmpty(code) || !GuidRx.IsMatch(code))
                code = installed.FirstOrDefault(a => !a.PerUser && !a.IsMarker && GuidRx.IsMatch(a.KeyName ?? ""))?.KeyName;
            if (!string.IsNullOrEmpty(code)) return new MsiPlan(code);
        }

        if (string.Equals(type, "command", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pkg.Uninstall!.Command))
            return new CmdPlan(pkg.Uninstall.Command!, pkg.Uninstall.Args ?? "");

        var q = installed.FirstOrDefault(a => !a.PerUser && !string.IsNullOrWhiteSpace(a.QuietUninstallString));
        if (q != null)
        {
            try
            {
                var (exe, args) = CommandLineSplitter.Split(q.QuietUninstallString!);
                return new CmdPlan(exe, args);
            }
            catch (InvalidDataException) { return null; }
        }
        return null;
    }

    public async Task<InstallOutcome> UninstallAsync(PackageEntry pkg, IReadOnlyList<InstalledApp> installed, string username,
        HardwareInfo hw, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        InstallOutcome outcome;
        string? detail = null;
        try
        {
            var plan = Resolve(pkg, installed);
            var timeout = TimeSpan.FromSeconds(pkg.TimeoutSec);
            switch (plan)
            {
                case null:
                    outcome = new InstallOutcome(InstallStatus.Blocked, null, "Gói này không hỗ trợ gỡ cài đặt tự động");
                    break;
                case ZipPlan z:
                    {
                        var r = PostActionRunner.Run(new[] { new PostAction("delete", null, z.Dir) }, "")[0];
                        outcome = r.Ok ? new InstallOutcome(InstallStatus.Success, 0, "Đã gỡ cài đặt")
                            : new InstallOutcome(InstallStatus.Failed, null, "Không xóa được thư mục cài đặt", r.Error);
                        break;
                    }
                case MsiPlan m:
                    {
                        var code = m.ProductCode;
                        outcome = await MsiInstaller.RunMsiAsync(
                            () => MsiNative.MsiConfigureProductExW(code, 0, MsiNative.INSTALLSTATE_ABSENT, "REBOOT=ReallySuppress"),
                            timeout, progress, ct).ConfigureAwait(false);
                        break;
                    }
                case CmdPlan c:
                    {
                        var exe = Environment.ExpandEnvironmentVariables(c.Command);
                        if (!Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\") || exe.StartsWith("//")
                            || exe.StartsWith(@"\/") || exe.StartsWith(@"/\"))
                            throw new UnsafePathException(Strings.UnsafePath);
                        SafePath.EnsureNoReparseAncestors(Path.GetFullPath(exe));
                        outcome = await exeRunner(InstallerType.Exe).RunAsync(
                            new InstallRequest(InstallerType.Exe, exe, c.Args, null, timeout), progress, ct).ConfigureAwait(false);
                        break;
                    }
                default:
                    throw new InvalidOperationException();
            }
        }
        catch (OperationCanceledException) { outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.Cancelled); }
        catch (Exception ex)
        {
            outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.ForUser(ex));
            detail = ex.ToString();
        }

        string? markerError = null;
        if (outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired)
        {
            try { marker.Remove(pkg.Id); }
            catch (Exception ex) { markerError = "Không xóa được dấu cài đặt: " + ex.Message; }
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(outcome.Message)) parts.Add(outcome.Message);
        if (detail != null) parts.Add(detail);
        if (!string.IsNullOrEmpty(outcome.Detail)) parts.Add(outcome.Detail);
        if (markerError != null) parts.Add(markerError);
        try
        {
            repo.WriteLog(new LogEntry(DateTimeOffset.Now, username, hw.Hostname, hw.BiosSerial, "uninstall",
                pkg.Id, pkg.Version, outcome.Status.ToString(), outcome.ExitCode,
                parts.Count > 0 ? string.Join("; ", parts) : null));
        }
        catch { }
        return outcome;
    }
}
