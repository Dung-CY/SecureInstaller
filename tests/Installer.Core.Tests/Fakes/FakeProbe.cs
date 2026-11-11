using Installer.Core.Platform;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeProbe : IConnectivityProbe
{
    public bool Result { get; set; }
    public int Calls { get; private set; }
    public Exception? Throw { get; set; }
    public FakeProbe(bool result = false) { Result = result; }
    public Task<bool> HasInternetAsync(CancellationToken ct) { Calls++; if (Throw != null) throw Throw; return Task.FromResult(Result); }
}
