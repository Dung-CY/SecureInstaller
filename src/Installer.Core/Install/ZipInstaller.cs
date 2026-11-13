using System.IO.Compression;
using Installer.Core.Errors;
using Installer.Core.Platform;

namespace Installer.Core.Install;

public sealed class ZipInstaller : IInstallerRunner
{
    public Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
        => Task.Run(() => Run(req, progress, ct));

    private static InstallOutcome Run(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TargetDir))
            return new InstallOutcome(InstallStatus.Failed, null, "Thiếu thư mục đích");
        var created = new List<string>();   // files and folders this run created, removed again on failure or cancel
        bool ok = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(req.TargetDir));
            SafePath.EnsureNoReparseAncestors(target);
            CreateDir(target, created);
            var prefix = Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar;

            using var za = ZipFile.OpenRead(req.InstallerPath);
            var entries = za.Entries.ToList();
            var dests = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].FullName.Contains(':'))
                    return new InstallOutcome(InstallStatus.Failed, null, "Gói zip không hợp lệ");
                var full = Path.GetFullPath(Path.Combine(target, entries[i].FullName));
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new InstallOutcome(InstallStatus.Failed, null, "Gói zip không hợp lệ");
                dests[i] = full;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                // A folder under the target may have been swapped for a junction since the last install.
                SafePath.EnsureNoReparseAncestors(dests[i]);
                if (entries[i].Name == "")
                    CreateDir(dests[i], created);
                else
                {
                    CreateDir(Path.GetDirectoryName(dests[i])!, created);
                    if (!File.Exists(dests[i])) created.Add(dests[i]);
                    entries[i].ExtractToFile(dests[i], overwrite: true);
                }
                progress.Report(new InstallProgress((i + 1) * 100 / entries.Count, "Đang giải nén"));
            }
            progress.Report(new InstallProgress(100, "Hoàn tất"));
            ok = true;
            return new InstallOutcome(InstallStatus.Success, 0, "Cài đặt thành công");
        }
        catch (UnsafePathException)
        {
            return new InstallOutcome(InstallStatus.Failed, null, Strings.UnsafePath);
        }
        catch (OperationCanceledException)
        {
            return new InstallOutcome(InstallStatus.Failed, null, Errors.ErrorText.Cancelled);
        }
        catch (Exception ex)
        {
            return new InstallOutcome(InstallStatus.Failed, null, Errors.ErrorText.ForUser(ex), ex.ToString());
        }
        finally
        {
            if (!ok) Cleanup(created);
        }
    }

    // shortcut: a file that existed before and was overwritten keeps the new content, upgrade to backups if partial upgrades bite.
    private static void Cleanup(List<string> created)
    {
        for (int i = created.Count - 1; i >= 0; i--)   // newest first, so folders are empty when reached
        {
            try
            {
                if (File.Exists(created[i])) File.Delete(created[i]);
                else if (Directory.Exists(created[i]) && !Directory.EnumerateFileSystemEntries(created[i]).Any()) Directory.Delete(created[i]);
            }
            catch (Exception) { }
        }
    }

    private static void CreateDir(string dir, List<string> created)
    {
        var missing = new Stack<string>();
        for (var d = dir; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Push(d);
        foreach (var d in missing) { Directory.CreateDirectory(d); created.Add(d); }
    }
}
