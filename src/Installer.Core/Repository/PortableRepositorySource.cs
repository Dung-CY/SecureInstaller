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
        if (!VaultStore.IsSafeFileName(pkg.BlobFile))
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

    public Task<byte[]> GetPackageKeyAsync(PackageEntry pkg, CancellationToken ct) => Task.FromResult(Decode(pkg.KeyB64));

    public Task<byte[]> GetDocsKeyAsync(PackageEntry pkg, CancellationToken ct) =>
        Task.FromResult(Decode(pkg.DocsKeyB64 ?? pkg.KeyB64));

    static byte[] Decode(string b64)
    {
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { throw new PackageCorruptException(Strings.PackageCorrupt); }
    }

    public void WriteLog(LogEntry entry) => _log.Append(entry);
}
