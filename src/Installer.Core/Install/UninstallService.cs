using System.Text.RegularExpressions;
using Installer.Core.Errors;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Platform;
using Installer.Core.Repository;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.Core.Install;

public sealed class UninstallService(IRepositorySource repo, InstallMarker marker, Func<InstallerType, IInstallerRunner> exeRunner,
    Func<InstalledApp, bool>? keyExists = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<TempWorkspace>? workspaceFactory = null)
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);
    private static readonly Regex InnoUninstaller = new(@"^unins\d{3}\.exe\z", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GuidRx = new(@"^\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}\z", RegexOptions.Compiled);

    private abstract record Plan;
    private sealed record ZipPlan(string Dir) : Plan;
    private sealed record MsiPlan(string ProductCode) : Plan;
    private sealed record CmdPlan(string Exe, string Args, InstalledApp? Row = null) : Plan;
    private sealed record ScriptPlan : Plan;

    private static readonly Regex MsiexecGuid = new(@"(?:^|\s)[/-][XIxi]\s*(\{[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\})", RegexOptions.Compiled);

    /// <summary>True exactly when a valid plan exists (all validation happens in <see cref="Resolve"/>).</summary>
    public bool CanUninstall(PackageEntry pkg, IReadOnlyList<InstalledApp> installed) => Resolve(pkg, installed) != null;

    /// <summary>
    /// Picks and validates the first applicable strategy; null when none applies or validation fails.
    /// Order: an explicit manifest <c>Uninstall.Type</c> (zip, msi, command) yields only its own plan or null (never falls through) and wins over strategies derived from
    /// <c>InstallerType</c>; after that: zip (InstallerType), msi (InstallerType), then a machine-wide (HKLM)
    /// QuietUninstallString. HKCU (PerUser) data and marker rows are never used.
    /// </summary>
    private static Plan? Resolve(PackageEntry pkg, IReadOnlyList<InstalledApp> installed)
    {
        try { return ResolveCore(pkg, installed); }
        catch (Exception) { return null; }
    }

    private static Plan? ResolveCore(PackageEntry pkg, IReadOnlyList<InstalledApp> installed)
    {
        var type = pkg.Uninstall?.Type;
        bool Is(string t) => string.Equals(type, t, StringComparison.OrdinalIgnoreCase);

        if (Is("zip")) return ZipFor(pkg);
        if (Is("msi")) return MsiFor(pkg, installed);
        if (Is("command"))
            return string.IsNullOrWhiteSpace(pkg.Uninstall!.Command) ? null
                : ValidExe(pkg.Uninstall.Command!, pkg.Uninstall.Args ?? "");

        if (!string.IsNullOrWhiteSpace(type)) return null; // explicit but unknown type: never fall through

        if (pkg.InstallerType == InstallerType.Script) return new ScriptPlan(); // runs uninstall_script if any, then the marker goes
        if (pkg.InstallerType == InstallerType.Zip) return ZipFor(pkg);
        if (pkg.InstallerType == InstallerType.Msi && MsiFor(pkg, installed) is { } m2) return m2;

        var rows = installed.Where(a => !a.PerUser && !a.IsMarker).ToList();
        var q = rows.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.QuietUninstallString));
        if (q != null) return FromCommandLine(q.QuietUninstallString!, null) is CmdPlan qc ? qc with { Row = q } : FromCommandLine(q.QuietUninstallString!, null);
        var u = rows.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.UninstallString));
        return u == null ? null : FromCommandLine(u.UninstallString!, pkg.Kind) is CmdPlan uc ? uc with { Row = u } : FromCommandLine(u.UninstallString!, pkg.Kind);
    }

    /// <summary>kind == null: use the command as is (QuietUninstallString); otherwise add the silent switches of that installer kind.</summary>
    private static Plan? FromCommandLine(string commandLine, InstallerKind? kind)
    {
        try
        {
            var (exe, args) = CommandLineSplitter.Split(commandLine);
            var name = Path.GetFileName(exe.Replace('/', '\\'));
            if (name.Equals("msiexec", StringComparison.OrdinalIgnoreCase) || name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                var g = MsiexecGuid.Match(args);
                return g.Success ? new MsiPlan(g.Groups[1].Value) : null; // never run msiexec by name / PATH
            }
            if (kind is not { } k) return ValidExe(exe, args);
            if (InnoUninstaller.IsMatch(name)) k = InstallerKind.Inno;
            string? extra = k switch
            {
                InstallerKind.Inno => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                InstallerKind.Nsis => "/S",
                InstallerKind.Burn => (Regex.IsMatch(args, @"(?:^|\s)[/-]uninstall(?:\s|\z)", RegexOptions.IgnoreCase) ? "" : "/uninstall ") + "/quiet /norestart",
                _ => null, // InstallShield, Msi without a GUID, Unknown, Zip, Script: no derived plan
            };
            return extra == null ? null : ValidExe(exe, (args + " " + extra).Trim());
        }
        catch (InvalidDataException) { return null; }
    }

    private static Plan? ZipFor(PackageEntry pkg) =>
        string.IsNullOrWhiteSpace(pkg.TargetDir) ? null : new ZipPlan(pkg.TargetDir);

    private static Plan? MsiFor(PackageEntry pkg, IReadOnlyList<InstalledApp> installed)
    {
        var code = pkg.Detect?.ProductCode?.Trim();
        if (string.IsNullOrEmpty(code) || !GuidRx.IsMatch(code))
            code = installed.FirstOrDefault(a => !a.PerUser && !a.IsMarker && GuidRx.IsMatch(a.KeyName ?? ""))?.KeyName;
        return string.IsNullOrEmpty(code) ? null : new MsiPlan(code);
    }

    private static Plan? ValidExe(string command, string args)
    {
        if (command.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null; // before expansion: it truncates at NUL
        var exe = Environment.ExpandEnvironmentVariables(command.Trim());
        if (exe.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        if (!Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\") || exe.StartsWith("//")
            || exe.StartsWith(@"\/") || exe.StartsWith(@"/\")) return null;
        try { SafePath.EnsureNoReparseAncestors(Path.GetFullPath(exe)); }
        catch (Exception) { return null; }
        return new CmdPlan(exe, args);
    }

    private async Task<bool> WaitGoneAsync(CmdPlan plan, IReadOnlyList<InstalledApp> installed, TimeSpan budget, CancellationToken ct)
    {
        // Only the row the plan targets: other matched rows (e.g. a bundle's children) may legitimately stay.
        var rows = plan.Row != null ? new List<InstalledApp> { plan.Row } : installed.Where(a => !a.PerUser && !a.IsMarker).ToList();
        for (var waited = TimeSpan.Zero; ; waited += PollEvery)
        {
            if (!rows.Any(keyExists!)) return true;
            if (waited >= budget) return false;
            await (delay ?? Task.Delay)(PollEvery, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Decrypts the package into a protected workspace (always deleted) and runs its uninstall script; no script means nothing to run.</summary>
    private async Task<(InstallOutcome, string?)> RunUninstallScriptAsync(PackageEntry pkg, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(pkg.UninstallScript)) return (new InstallOutcome(InstallStatus.Success, 0, Strings.Uninstalled), null);
        TempWorkspace? ws = null;
        try
        {
            ws = (workspaceFactory ?? (() => TempWorkspace.Create()))();
            if (!await InstallService.ExtractAsync(repo, pkg, ws, progress, ct).ConfigureAwait(false))
                return (new InstallOutcome(InstallStatus.Failed, null, Strings.PackageCorrupt), null);
            var dir = Path.GetFullPath(Path.Combine(ws.Path, "pkg", "scripts")) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(dir, pkg.UninstallScript));
            if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return (new InstallOutcome(InstallStatus.Failed, null, Strings.PackageCorrupt), null);
            var o = await new ScriptRunner().RunAsync(
                new InstallRequest(InstallerType.Script, path, "", null, TimeSpan.FromSeconds(pkg.ScriptTimeoutSec)), progress, ct).ConfigureAwait(false);
            return (o, null);
        }
        finally
        {
            try { ws?.Dispose(); } catch { }
        }
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
                    outcome = new InstallOutcome(InstallStatus.Blocked, null, Strings.NoUninstall);
                    break;
                case ZipPlan z:
                    {
                        var r = PostActionRunner.Run(new[] { new PostAction("delete", null, z.Dir) }, "")[0];
                        outcome = r.Ok ? new InstallOutcome(InstallStatus.Success, 0, Strings.Uninstalled)
                            : new InstallOutcome(InstallStatus.Failed, null, Strings.UninstallFolderFailed, r.Error);
                        break;
                    }
                case MsiPlan m:
                    {
                        var code = m.ProductCode;
                        outcome = await MsiInstaller.RunMsiAsync(
                            () => MsiNative.MsiConfigureProductExW(code, 0, MsiNative.INSTALLSTATE_ABSENT, "REBOOT=ReallySuppress"),
                            timeout, progress, ct).ConfigureAwait(false);
                        if (outcome.ExitCode == 1605) // product not installed: already gone
                            outcome = new InstallOutcome(InstallStatus.Success, 1605, Strings.Uninstalled);
                        break;
                    }
                case CmdPlan c:
                    outcome = await exeRunner(InstallerType.Exe).RunAsync(
                        new InstallRequest(InstallerType.Exe, c.Exe, c.Args, null, timeout), progress, ct).ConfigureAwait(false);
                    // Inno/NSIS uninstallers relaunch from %TEMP% and return at once: wait for the Uninstall key to go.
                    if (outcome.Status == InstallStatus.Success && keyExists != null
                        && !await WaitGoneAsync(c, installed, TimeSpan.FromSeconds(Math.Min(pkg.TimeoutSec, 120)), ct).ConfigureAwait(false))
                        outcome = new InstallOutcome(InstallStatus.Failed, outcome.ExitCode, Strings.UninstallIncomplete);
                    break;
                case ScriptPlan:
                    (outcome, detail) = await RunUninstallScriptAsync(pkg, progress, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException();
            }
            if (plan != null)
                outcome = outcome.Status switch
                {
                    InstallStatus.Success => outcome with { Message = Strings.Uninstalled },
                    InstallStatus.Timeout => outcome with { Message = Strings.UninstallTimeout },
                    _ => outcome,
                };
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
