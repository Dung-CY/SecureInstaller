namespace Installer.Core.Platform;

public interface IConnectivityProbe
{
    Task<bool> HasInternetAsync(CancellationToken ct);
}
