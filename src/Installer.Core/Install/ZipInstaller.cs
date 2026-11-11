using System.IO.Compression;

namespace Installer.Core.Install;

public sealed class ZipInstaller : IInstallerRunner
{
    public Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
        => Task.Run(() => Run(req, progress, ct), ct);

    private static InstallOutcome Run(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TargetDir))
            return new InstallOutcome(InstallStatus.Failed, null, "Thiếu thư mục đích");
        try
        {
            var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(req.TargetDir));
            Directory.CreateDirectory(target);
            var prefix = Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar;

            using var za = ZipFile.OpenRead(req.InstallerPath);
            var entries = za.Entries.ToList();
            var dests = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var full = Path.GetFullPath(Path.Combine(target, entries[i].FullName));
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new InstallOutcome(InstallStatus.Failed, null, "Gói zip không hợp lệ");
                dests[i] = full;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (entries[i].Name == "")
                    Directory.CreateDirectory(dests[i]);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dests[i])!);
                    entries[i].ExtractToFile(dests[i], overwrite: true);
                }
                progress.Report(new InstallProgress((i + 1) * 100 / entries.Count, "Đang giải nén"));
            }
            progress.Report(new InstallProgress(100, "Hoàn tất"));
            return new InstallOutcome(InstallStatus.Success, 0, "Cài đặt thành công");
        }
        catch (OperationCanceledException)
        {
            return new InstallOutcome(InstallStatus.Failed, null, "Người dùng hủy cài đặt");
        }
        catch (Exception ex)
        {
            return new InstallOutcome(InstallStatus.Failed, null, ex.Message);
        }
    }
}
