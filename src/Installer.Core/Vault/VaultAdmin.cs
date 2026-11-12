using Installer.Core.Crypto;
using Installer.Core.Model;

namespace Installer.Core.Vault;

public sealed class VaultAdmin
{
    readonly VaultSession _s;
    readonly Func<string, byte[]?>? _iconFromExe;

    public VaultAdmin(VaultSession session, Func<string, byte[]?>? iconFromExe = null)
    {
        if (!session.IsAdmin || session.SigningPrivateKey is null)
            throw new NotAdminException("Chỉ quản trị viên mới được thực hiện thao tác này");
        if (session.Store.IsReadOnly)
            throw new Errors.UserMessageException("Kho đang ở chế độ chỉ đọc");
        _s = session;
        _iconFromExe = iconFromExe;
    }

    string VaultDir => Path.Combine(_s.Store.Root, "vault");
    string DocsDir(PackageEntry e) => PackageBuilder.DocsDirFor(e, _s.Store.Root);
    string BlobPath(PackageEntry e) => Path.Combine(VaultDir, "packages", e.BlobFile);

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Writes a new catalog (Version++, UpdatedAt) and updates the session on success.
    void Commit(List<PackageEntry> packages, KeywordScanConfig? kw = null, List<CrackSignature>? sigs = null, List<Bundle>? bundles = null)
    {
        var c = _s.Catalog;
        var next = c with
        {
            Format = 2,
            Version = c.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            Packages = packages,
            KeywordScan = kw ?? c.KeywordScan,
            CrackSignatures = sigs ?? c.CrackSignatures,
            Bundles = bundles ?? c.Bundles
        };
        _s.Store.WriteCatalog(next, _s.VaultKey, _s.SigningPrivateKey!);
        _s.Catalog = next;
    }

    void DeleteFiles(PackageEntry e)
    {
        try { File.Delete(BlobPath(e)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        try { if (Directory.Exists(DocsDir(e))) Directory.Delete(DocsDir(e), true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public PackageEntry AddPackage(string sourceFolder)
    {
        var m = PackageBuilder.ReadManifest(sourceFolder).Entry;
        if (_s.Catalog.Packages.Any(p => Same(p.Id, m.Id) && Same(p.Version, m.Version)))
            throw new Errors.UserMessageException($"Gói {m.Id} phiên bản {m.Version} đã tồn tại");
        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root, _iconFromExe);
        try { Commit(_s.Catalog.Packages.Append(entry).ToList()); }
        catch (Exception ex) when (ex is not CatalogCommitPendingException) { DeleteFiles(entry); throw; }
        return entry;
    }

    public PackageEntry UpdatePackage(string id, string sourceFolder, bool keepOld)
    {
        var m = PackageBuilder.ReadManifest(sourceFolder).Entry;
        if (!Same(m.Id, id)) throw new InvalidDataException("Trường id trong manifest không khớp với gói cần cập nhật");
        if (!_s.Catalog.Packages.Any(p => Same(p.Id, id)))
            throw new Errors.UserMessageException("Không tìm thấy gói");

        if (keepOld && _s.Catalog.Packages.Any(p => Same(p.Id, id) && Same(p.Version, m.Version)))
            throw new Errors.UserMessageException($"Gói {m.Id} phiên bản {m.Version} đã tồn tại");

        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root, _iconFromExe);
        var removed = _s.Catalog.Packages.Where(p => Same(p.Id, id) && (!keepOld || Same(p.Version, m.Version))).ToList();
        var packages = _s.Catalog.Packages.Except(removed).Append(entry).ToList();
        try { Commit(packages); }
        catch (Exception ex) when (ex is not CatalogCommitPendingException) { DeleteFiles(entry); throw; }
        foreach (var old in removed) DeleteFiles(old);
        return entry;
    }

    public void RemovePackage(string id, string? version)
    {
        var removed = _s.Catalog.Packages
            .Where(p => Same(p.Id, id) && (version is null || Same(p.Version, version))).ToList();
        if (removed.Count == 0) throw new Errors.UserMessageException("Không tìm thấy gói");
        Commit(_s.Catalog.Packages.Except(removed).ToList());
        foreach (var e in removed) DeleteFiles(e);
    }

    public void AddUser(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Tên đăng nhập trống", nameof(username));
        var meta = _s.Store.Meta;
        if (meta.Users.Any(u => Same(u.Username, username)))
            throw new Errors.UserMessageException("Tên đăng nhập đã tồn tại");
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
                     ?? throw new Errors.UserMessageException("Không tìm thấy người dùng");
        if (target.WrappedSigningKeyB64 != null && meta.Users.Count(u => u.WrappedSigningKeyB64 != null) <= 1)
            throw new Errors.UserMessageException("Không thể xóa quản trị viên cuối cùng");
        _s.Store.UpdateMeta(meta with { Users = meta.Users.Where(u => u != target).ToList() });
    }

    public void SetPassword(string username, string newPassword)
    {
        var meta = _s.Store.Meta;
        var target = meta.Users.FirstOrDefault(u => Same(u.Username, username))
                     ?? throw new Errors.UserMessageException("Không tìm thấy người dùng");
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

    static readonly System.Text.RegularExpressions.Regex BundleId = new(@"^[A-Za-z0-9._-]+\z");

    void ValidateBundle(Bundle b)
    {
        if (string.IsNullOrEmpty(b.Id) || !BundleId.IsMatch(b.Id))
            throw new InvalidDataException("Mã bộ cài đặt không hợp lệ");
        if (string.IsNullOrWhiteSpace(b.Name)) throw new InvalidDataException("Tên bộ cài đặt trống");
        if (b.Items is null || b.Items.Count == 0) throw new InvalidDataException("Bộ cài đặt không có phần mềm nào");
        var seen = new HashSet<(string, string?)>();
        foreach (var it in b.Items)
            if (!seen.Add((it.PackageId.ToLowerInvariant(), it.Version?.ToLowerInvariant())))
                throw new InvalidDataException($"Bộ cài đặt có phần mềm bị lặp: {it.PackageId}");
        foreach (var (item, pkg) in BundleResolver.Resolve(_s.Catalog, b))
            if (pkg is null)
                throw new InvalidDataException($"Bộ cài đặt tham chiếu phần mềm không có trong kho: {item.PackageId}");
    }

    List<Bundle> Bundles => _s.Catalog.Bundles ?? new();

    public bool HasBundle(string id) => Bundles.Any(x => Same(x.Id, id));

    public Bundle AddBundle(Bundle b)
    {
        ValidateBundle(b);
        if (Bundles.Any(x => Same(x.Id, b.Id))) throw new Errors.UserMessageException("Bộ cài đặt đã tồn tại");
        Commit(_s.Catalog.Packages, bundles: Bundles.Append(b).ToList());
        return b;
    }

    public Bundle UpdateBundle(Bundle b)
    {
        ValidateBundle(b);
        if (!Bundles.Any(x => Same(x.Id, b.Id))) throw new Errors.UserMessageException("Không tìm thấy bộ cài đặt");
        Commit(_s.Catalog.Packages, bundles: Bundles.Select(x => Same(x.Id, b.Id) ? b : x).ToList());
        return b;
    }

    public void RemoveBundle(string id)
    {
        if (!Bundles.Any(x => Same(x.Id, id))) throw new Errors.UserMessageException("Không tìm thấy bộ cài đặt");
        Commit(_s.Catalog.Packages, bundles: Bundles.Where(x => !Same(x.Id, id)).ToList());
    }
}
