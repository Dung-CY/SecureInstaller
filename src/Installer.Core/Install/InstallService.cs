using System.IO.Compression;
using System.Security.Cryptography;
using Installer.Core.Crypto;
using Installer.Core.Errors;
using Installer.Core.Logging;
using Installer.Core.Repository;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Vault;

namespace Installer.Core.Install;

public sealed class InstallService
{
    private readonly IRepositorySource _repo;
    private readonly InstallerRunnerFactory _runners;
    private readonly Func<TempWorkspace> _workspaceFactory;

    public InstallService(IRepositorySource repo, InstallerRunnerFactory runners, Func<TempWorkspace>? workspaceFactory = null)
    {
        _repo = repo; _runners = runners; _workspaceFactory = workspaceFactory ?? (() => TempWorkspace.Create());
    }

    private static string DocsBase => Path.Combine(Path.GetTempPath(), "SI_docs");

    public async Task<InstallOutcome> InstallAsync(PackageEntry pkg, string username, HardwareInfo hw,
        IProgress<InstallProgress> progress, CancellationToken ct)
    {
        InstallOutcome outcome;
        string? detail = null;
        TempWorkspace? ws = null;
        try
        {
            ws = _workspaceFactory();
            outcome = await RunCoreAsync(pkg, ws, progress, ct, d => detail = d).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.Cancelled); }
        catch (Exception ex)
        {
            outcome = new InstallOutcome(InstallStatus.Failed, null, ErrorText.ForUser(ex));
            detail = ex.ToString();
        }
        finally { try { ws?.Dispose(); } catch { } }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(outcome.Message)) parts.Add(outcome.Message);
        if (detail != null) parts.Add(detail);
        if (!string.IsNullOrEmpty(outcome.Detail)) parts.Add(outcome.Detail);
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

    private async Task<InstallOutcome> RunCoreAsync(PackageEntry pkg, TempWorkspace ws, IProgress<InstallProgress> progress,
        CancellationToken ct, Action<string> setDetail)
    {
        var corrupt = new InstallOutcome(InstallStatus.Failed, null, Strings.PackageCorrupt);
        var encPath = Path.Combine(ws.Path, "pkg.enc");
        var zipPath = Path.Combine(ws.Path, "pkg.zip");
        var pkgDir = Path.Combine(ws.Path, "pkg");

        progress.Report(new InstallProgress(0, "Đang kiểm tra gói"));
        string actual;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using var src = await _repo.OpenPackageBlobAsync(pkg, ct).ConfigureAwait(false);
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
        if (!string.Equals(actual, pkg.Sha256, StringComparison.OrdinalIgnoreCase)) return corrupt;

        ct.ThrowIfCancellationRequested();
        progress.Report(new InstallProgress(null, "Đang giải mã"));
        ct.ThrowIfCancellationRequested();
        byte[] key;
        try { key = await _repo.GetPackageKeyAsync(pkg, ct).ConfigureAwait(false); }
        catch (PackageCorruptException) { return corrupt; }
        try
        {
            await Task.Run(() =>
            {
                using var cipher = new FileStream(encPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var plain = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                PackageCrypto.Decrypt(cipher, plain, key);
            }, ct).ConfigureAwait(false);
        }
        catch (PackageCorruptException) { return corrupt; }
        try { File.Delete(encPath); } catch { }

        ct.ThrowIfCancellationRequested();
        try { await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, pkgDir, true), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return corrupt; }
        try { File.Delete(zipPath); } catch { }
        ct.ThrowIfCancellationRequested();

        var installerDir = Path.GetFullPath(Path.Combine(pkgDir, "installer")) + Path.DirectorySeparatorChar;
        var installerPath = Path.GetFullPath(Path.Combine(pkgDir, "installer", pkg.InstallerFile));
        if (!installerPath.StartsWith(installerDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(installerPath))
            return corrupt;

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
