using Installer.Core.Model;

namespace Installer.Core.Install;

public enum InstallStatus { Success, RebootRequired, Failed, Timeout, Blocked }

public record InstallProgress(int? Percent, string Step);

public record InstallRequest(InstallerType Type, string InstallerPath, string SilentArgs, string? TargetDir, TimeSpan Timeout);

public record InstallOutcome(InstallStatus Status, int? ExitCode, string Message);

public interface IInstallerRunner
{
    Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct);
}

public sealed class InstallerRunnerFactory
{
    private readonly Func<InstallerType, IInstallerRunner>? _override;

    public InstallerRunnerFactory() { }

    public InstallerRunnerFactory(Func<InstallerType, IInstallerRunner>? overrideFactory) => _override = overrideFactory;

    public IInstallerRunner For(InstallerType t) => _override?.Invoke(t) ?? t switch
    {
        InstallerType.Msi => new MsiInstaller(),
        InstallerType.Exe => new ExeInstaller(),
        InstallerType.Zip => new ZipInstaller(),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };
}
