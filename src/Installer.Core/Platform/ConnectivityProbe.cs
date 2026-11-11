namespace Installer.Core.Platform;

public sealed class ConnectivityProbe : IConnectivityProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<bool> HasInternetAsync(CancellationToken ct)
    {
        try
        {
            var body = await Http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt", ct).ConfigureAwait(false);
            return body.Trim() == "Microsoft Connect Test";
        }
        catch
        {
            return false;
        }
    }
}
