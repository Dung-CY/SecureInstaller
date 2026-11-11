using Installer.Core.Logging;
using Installer.Core.Model;

namespace Installer.Core.Repository;

public interface IRepositorySource
{
    Catalog Catalog { get; }
    Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct);
    Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct);
    void WriteLog(LogEntry entry);
}
