using Installer.Core.Platform;

namespace Installer.Core.Tests.Fakes;

public sealed class FakeProbe : IConnectivityProbe
{
    public bool Result { get; set; }
    public int Calls { get; private set; }
    public FakeProbe(bool result = false) { Result = result; }
    public Task<bool> HasInternetAsync(CancellationToken ct) { Calls++; return Task.FromResult(Result); }
}
