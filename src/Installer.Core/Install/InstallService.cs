using System.IO.Compression;
using System.Security.Cryptography;
using Installer.Core.Crypto;
using Installer.Core.Errors;
using Installer.Core.Logging;
using Installer.Core.Repository;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Vault;
using Installer.Core.Versioning;

namespace Installer.Core.Install;

public sealed class InstallService
{
    private readonly IRepositorySource _repo;
    private readonly InstallerRunnerFactory _runners;
    private readonly Func<TempWorkspace> _workspaceFactory;

    private readonly InstallMarker? _marker;
    private readonly InstalledAppsScanner? _scanner;

    public InstallService(IRepositorySource repo, InstallerRunnerFactory runners, Func<TempWorkspace>? workspaceFactory = null,
        InstallMarker? marker = null, InstalledAppsScanner? scanner = null)
    {
        _marker = marker; _scanner = scanner;
        _repo = repo; _runners = runners; _workspaceFactory = workspaceFactory ?? (() => TempWorkspace.Create());
    }

    private static string DocsBase => Path.Combine(Path.GetTempPath(), "SI_docs");

    public async Task<InstallOutcome> InstallAsync(PackageEntry pkg, string username, HardwareInfo hw,
        IProgress<InstallProgress> progress, CancellationToken ct)
    {
        InstallOutcome outcome;
        string? detail = null;
        TempWorkspace? ws = null;
        HashSet<string>? before = null;
        if (_scanner != null && pkg.InstallerType is InstallerType.Exe or InstallerType.Msi)
            try { before = _scanner.SnapshotMachineKeys(); } catch (Exception) { }
        try
        {
            ws = _workspaceFactory();
            outcome = await RunCoreAsync(pkg, ws, progress, ct, d => detail = detail == null ? d : detail + "; " + d).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.Cancelled); }
        catch (Exception ex)
        {
            outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.ForUser(ex));
            detail = ex.ToString();
        }
        finally { try { ws?.Dispose(); } catch { } }

        string? markerError = null;
        if (_marker != null && outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired)
        {
            string? uninstallKey = null;
            if (before != null) try { uninstallKey = _scanner!.PickNewKey(before, pkg.Detect, pkg); } catch (Exception) { }
            try { _marker.Write(pkg.Id, pkg.Version, DateTimeOffset.Now, uninstallKey); }
            catch (Exception ex) { markerError = "Không ghi được dấu cài đặt: " + ex.Message; }
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(outcome.Message)) parts.Add(outcome.Message);
        if (detail != null) parts.Add(detail);
        if (!string.IsNullOrEmpty(outcome.Detail)) parts.Add(outcome.Detail);
        if (markerError != null) parts.Add(markerError);
        if (ws != null && !ws.Deleted) parts.Add("Không xóa được thư mục tạm");
        detail = parts.Count > 0 ? string.Join("; ", parts) : null;
        try
        {
            _repo.WriteLog(new LogEntry(DateTimeOffset.Now, username, hw.Hostname, hw.BiosSerial, "install",
                pkg.Id, pkg.Version, outcome.Status.ToString(), outcome.ExitCode, detail));
        }
        catch { }
        return outcome;
    }

    /// <summary>Verifies SHA-256, decrypts and unzips the package into <c>ws\pkg</c>; false when it is corrupt. Shared with the script uninstall.</summary>
    internal static async Task<bool> ExtractAsync(IRepositorySource repo, PackageEntry pkg, TempWorkspace ws,
        IProgress<InstallProgress> progress, CancellationToken ct)
    {
        var encPath = Path.Combine(ws.Path, "pkg.enc");
        var zipPath = Path.Combine(ws.Path, "pkg.zip");
        var pkgDir = Path.Combine(ws.Path, "pkg");

        progress.Report(new InstallProgress(0, "Đang kiểm tra phần mềm"));
        string actual;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using var src = await repo.OpenPackageBlobAsync(pkg, ct).ConfigureAwait(false);
            await using var dst = new FileStream(encPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buf = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buf, 0, n);
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            }
            actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        if (!string.Equals(actual, pkg.Sha256, StringComparison.OrdinalIgnoreCase)) return false;

        ct.ThrowIfCancellationRequested();
        progress.Report(new InstallProgress(null, "Đang giải mã"));
        ct.ThrowIfCancellationRequested();
        byte[] key;
        try { key = await repo.GetPackageKeyAsync(pkg, ct).ConfigureAwait(false); }
        catch (PackageCorruptException) { return false; }
        try
        {
            await Task.Run(() =>
            {
                using var cipher = new FileStream(encPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var plain = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                PackageCrypto.Decrypt(cipher, plain, key);
            }, ct).ConfigureAwait(false);
        }
        catch (PackageCorruptException) { return false; }
        try { File.Delete(encPath); } catch { }

        ct.ThrowIfCancellationRequested();
        try { await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, pkgDir, true), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
        try { File.Delete(zipPath); } catch { }
        ct.ThrowIfCancellationRequested();
        return true;
    }

    private async Task<InstallOutcome> RunCoreAsync(PackageEntry pkg, TempWorkspace ws, IProgress<InstallProgress> progress,
        CancellationToken ct, Action<string> setDetail)
    {
        var corrupt = new InstallOutcome(InstallStatus.Failed, null, Strings.PackageCorrupt);
        var pkgDir = Path.Combine(ws.Path, "pkg");
        if (!await ExtractAsync(_repo, pkg, ws, progress, ct).ConfigureAwait(false)) return corrupt;

        var sub = pkg.InstallerType == InstallerType.Script ? "scripts" : "installer";
        var installerDir = Path.GetFullPath(Path.Combine(pkgDir, sub)) + Path.DirectorySeparatorChar;
        var installerPath = Path.GetFullPath(Path.Combine(pkgDir, sub, pkg.InstallerFile));
        if (!installerPath.StartsWith(installerDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(installerPath))
            return corrupt;

        // Hook scripts always use the real ScriptRunner (not the per-type factory): they are configuration, not the installer.
        var scriptsDir = Path.GetFullPath(Path.Combine(pkgDir, "scripts")) + Path.DirectorySeparatorChar;
        string? HookPath(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var p = Path.GetFullPath(Path.Combine(scriptsDir, name));
            return p.StartsWith(scriptsDir, StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? p : "";
        }
        Task<InstallOutcome> RunHook(string path) => new ScriptRunner().RunAsync(
            new InstallRequest(InstallerType.Script, path, "", null, TimeSpan.FromSeconds(pkg.ScriptTimeoutSec)), progress, ct);

        var prePath = HookPath(pkg.PreScript);
        var postPath = HookPath(pkg.PostScript);
        if (prePath == "" || postPath == "") return corrupt; // listed hook missing or outside scripts\
        if (prePath != null)
        {
            var pre = await RunHook(prePath).ConfigureAwait(false);
            if (pre.Status != InstallStatus.Success)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                setDetail(pre.Detail ?? pre.Message);
                return new InstallOutcome(InstallStatus.Failed, pre.ExitCode, "Script trước khi cài lỗi");
            }
            if (!string.IsNullOrWhiteSpace(pre.Detail)) setDetail("pre_script: " + pre.Detail.Trim());
        }

        var req = new InstallRequest(pkg.InstallerType, installerPath, pkg.SilentArgs, pkg.TargetDir,
            TimeSpan.FromSeconds(pkg.TimeoutSec));
        var outcome = await _runners.For(pkg.InstallerType).RunAsync(req, progress, ct).ConfigureAwait(false);

        if (outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired)
        {
            var failed = PostActionRunner.Run(pkg.PostActions, Path.Combine(pkgDir, "extras")).Where(r => !r.Ok).ToList();
            if (failed.Count > 0)
            {
                outcome = outcome with { Message = outcome.Message + $" ({failed.Count} thao tác sau cài lỗi)" };
                setDetail(string.Join("; ", failed.Select(f => $"{f.Action.Dst}: {f.Error}")));
            }
            if (postPath != null)
            {
                var post = await RunHook(postPath).ConfigureAwait(false);
                if (post.Status != InstallStatus.Success)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    outcome = outcome with { Message = outcome.Message + " (Cấu hình sau cài lỗi)" };
                    setDetail(post.Detail ?? post.Message);
                }
                else if (!string.IsNullOrWhiteSpace(post.Detail)) setDetail("post_script: " + post.Detail.Trim());
            }
        }
        return outcome;
    }

    public async Task<string> ExtractDocAsync(PackageEntry pkg, string docName, CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(DocsBase))
                foreach (var d in Directory.EnumerateDirectories(DocsBase))
                    try { Directory.Delete(d, true); } catch { }
        }
        catch { }

        byte[] key;
        key = await _repo.GetDocsKeyAsync(pkg, ct).ConfigureAwait(false);
        await using var src = await _repo.OpenDocBlobAsync(pkg, docName, ct).ConfigureAwait(false);
        var dir = Path.Combine(DocsBase, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, docName);
        try
        {
            await Task.Run(() =>
            {
                using var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                PackageCrypto.Decrypt(src, dst, key);
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            try { Directory.Delete(dir, true); } catch { }
            throw;
        }
        return path;
    }

    public static void CleanupDocs()
    {
        try { if (Directory.Exists(DocsBase)) Directory.Delete(DocsBase, true); } catch { }
    }
}
