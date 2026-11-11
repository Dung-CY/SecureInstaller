using Installer.Core.Install;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeRunner : IInstallerRunner
{
    public List<InstallRequest> Requests { get; } = new();
    public bool InstallerExistedAtCall { get; private set; }
    public InstallOutcome Outcome { get; set; } = new(InstallStatus.Success, 0, "OK");
    public Exception? Throw { get; set; }

    public Task<InstallOutcome> RunAsync(InstallRequest req, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        Requests.Add(req);
        InstallerExistedAtCall = File.Exists(req.InstallerPath);
        if (Throw != null) throw Throw;
        return Task.FromResult(Outcome);
    }
}
