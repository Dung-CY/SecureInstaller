using Installer.Core.Crypto;
using Installer.Core.Model;

namespace Installer.Core.Vault;

public sealed class VaultAdmin
{
    readonly VaultSession _s;

    public VaultAdmin(VaultSession session)
    {
        if (!session.IsAdmin || session.SigningPrivateKey is null)
            throw new NotAdminException("Chỉ quản trị viên mới được thực hiện thao tác này");
        if (session.Store.IsReadOnly)
            throw new InvalidOperationException("Kho đang ở chế độ chỉ đọc");
        _s = session;
    }

    string VaultDir => Path.Combine(_s.Store.Root, "vault");
    string DocsDir(PackageEntry e) => Path.Combine(VaultDir, "docs", $"{e.Id}-{e.Version}");
    string BlobPath(PackageEntry e) => Path.Combine(VaultDir, "packages", e.BlobFile);

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Writes a new catalog (Version++, UpdatedAt) and updates the session on success.
    void Commit(List<PackageEntry> packages, KeywordScanConfig? kw = null, List<CrackSignature>? sigs = null)
    {
        var c = _s.Catalog;
        var next = c with
        {
            Version = c.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            Packages = packages,
            KeywordScan = kw ?? c.KeywordScan,
            CrackSignatures = sigs ?? c.CrackSignatures
        };
        _s.Store.WriteCatalog(next, _s.VaultKey, _s.SigningPrivateKey!);
        _s.Catalog = next;
    }

    void DeleteFiles(PackageEntry e)
    {
        try { File.Delete(BlobPath(e)); } catch (IOException) { }
        try { if (Directory.Exists(DocsDir(e))) Directory.Delete(DocsDir(e), true); } catch (IOException) { }
    }

    public PackageEntry AddPackage(string sourceFolder)
    {
        var m = PackageBuilder.ReadManifest(sourceFolder).Entry;
        if (_s.Catalog.Packages.Any(p => Same(p.Id, m.Id) && Same(p.Version, m.Version)))
            throw new InvalidOperationException($"Gói {m.Id} phiên bản {m.Version} đã tồn tại");
        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root);
        try { Commit(_s.Catalog.Packages.Append(entry).ToList()); }
        catch { DeleteFiles(entry); throw; }
        return entry;
    }

    public PackageEntry UpdatePackage(string id, string sourceFolder, bool keepOld)
    {
        var m = PackageBuilder.ReadManifest(sourceFolder).Entry;
        if (!Same(m.Id, id)) throw new InvalidDataException("Trường id trong manifest không khớp với gói cần cập nhật");
        if (!_s.Catalog.Packages.Any(p => Same(p.Id, id)))
            throw new InvalidOperationException("Không tìm thấy gói");

        var sameVersion = _s.Catalog.Packages.FirstOrDefault(p => Same(p.Id, id) && Same(p.Version, m.Version));
        if (sameVersion != null && keepOld)
            throw new InvalidOperationException($"Gói {m.Id} phiên bản {m.Version} đã tồn tại");

        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root);
        var removed = _s.Catalog.Packages.Where(p => Same(p.Id, id) && (!keepOld || Same(p.Version, m.Version))).ToList();
        var packages = _s.Catalog.Packages.Except(removed).Append(entry).ToList();
        try { Commit(packages); }
        catch { DeleteFiles(entry); throw; }
        // A same-version entry shares file names with the new one, which were overwritten in place.
        foreach (var old in removed.Where(p => !Same(p.Version, m.Version))) DeleteFiles(old);
        return entry;
    }

    public void RemovePackage(string id, string? version)
    {
        var removed = _s.Catalog.Packages
            .Where(p => Same(p.Id, id) && (version is null || Same(p.Version, version))).ToList();
        if (removed.Count == 0) throw new InvalidOperationException("Không tìm thấy gói");
        Commit(_s.Catalog.Packages.Except(removed).ToList());
        foreach (var e in removed) DeleteFiles(e);
    }

    public void AddUser(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Tên đăng nhập trống", nameof(username));
        var meta = _s.Store.Meta;
        if (meta.Users.Any(u => Same(u.Username, username)))
            throw new InvalidOperationException("Tên đăng nhập đã tồn tại");
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek(password, salt);
        var user = new UserEntry(username, role, Convert.ToBase64String(salt),
            Convert.ToBase64String(KeyWrap.Wrap(_s.VaultKey, kek)),
            role == UserRole.Admin ? Convert.ToBase64String(KeyWrap.Wrap(_s.SigningPrivateKey!, kek)) : null, 0, null);
        _s.Store.UpdateMeta(meta with { Users = meta.Users.Append(user).ToList() });
    }

    public void RemoveUser(string username)
    {
        var meta = _s.Store.Meta;
        var target = meta.Users.FirstOrDefault(u => Same(u.Username, username))
                     ?? throw new InvalidOperationException("Không tìm thấy người dùng");
        if (target.WrappedSigningKeyB64 != null && meta.Users.Count(u => u.WrappedSigningKeyB64 != null) <= 1)
            throw new InvalidOperationException("Không thể xóa quản trị viên cuối cùng");
        _s.Store.UpdateMeta(meta with { Users = meta.Users.Where(u => u != target).ToList() });
    }

    public void SetPassword(string username, string newPassword)
    {
        var meta = _s.Store.Meta;
        var target = meta.Users.FirstOrDefault(u => Same(u.Username, username))
                     ?? throw new InvalidOperationException("Không tìm thấy người dùng");
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek(newPassword, salt);
        var updated = target with
        {
            SaltB64 = Convert.ToBase64String(salt),
            WrappedVkB64 = Convert.ToBase64String(KeyWrap.Wrap(_s.VaultKey, kek)),
            WrappedSigningKeyB64 = target.WrappedSigningKeyB64 == null
                ? null : Convert.ToBase64String(KeyWrap.Wrap(_s.SigningPrivateKey!, kek)),
            FailedAttempts = 0,
            LockedUntil = null
        };
        _s.Store.UpdateMeta(meta with { Users = meta.Users.Select(u => u == target ? updated : u).ToList() });
    }

    public void UpdateSettings(KeywordScanConfig keywordScan, List<CrackSignature> signatures) =>
        Commit(_s.Catalog.Packages, keywordScan, signatures);
}
