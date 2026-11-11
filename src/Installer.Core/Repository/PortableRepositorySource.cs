using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.Core.Repository;

public sealed class PortableRepositorySource : IRepositorySource
{
    private readonly VaultSession _session;
    private readonly EncryptedLog _log;

    public PortableRepositorySource(VaultSession session, EncryptedLog log) { _session = session; _log = log; }

    public Catalog Catalog => _session.Catalog;

    public Task<Stream> OpenPackageBlobAsync(PackageEntry pkg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(pkg.BlobFile) || pkg.BlobFile != Path.GetFileName(pkg.BlobFile)
            || pkg.BlobFile is "." or "..")
            throw new PackageCorruptException(Strings.PackageCorrupt);
        var path = Path.Combine(_session.Store.Root, "vault", "packages", pkg.BlobFile);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }

    public Task<Stream> OpenDocBlobAsync(PackageEntry pkg, string docName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(docName) || docName != Path.GetFileName(docName) || docName is "." or ".."
            || !pkg.Docs.Contains(docName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Tên tài liệu không hợp lệ", nameof(docName));
        var path = Path.Combine(PackageBuilder.DocsDirFor(pkg, _session.Store.Root), docName + ".enc");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }

    public void WriteLog(LogEntry entry) => _log.Append(entry);
}
