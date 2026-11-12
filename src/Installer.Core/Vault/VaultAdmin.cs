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
            throw new Errors.UserMessageException($"Phần mềm {m.Id} phiên bản {m.Version} đã tồn tại");
        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root, _iconFromExe);
        try { Commit(_s.Catalog.Packages.Append(entry).ToList()); }
        catch (Exception ex) when (ex is not CatalogCommitPendingException) { DeleteFiles(entry); throw; }
        return entry;
    }

    /// <param name="replaceVersion">Edit of one stored version: only that version is replaced, other versions stay.</param>
    public PackageEntry UpdatePackage(string id, string sourceFolder, bool keepOld, string? replaceVersion = null)
    {
        var m = PackageBuilder.ReadManifest(sourceFolder).Entry;
        if (!Same(m.Id, id)) throw new InvalidDataException("Trường id trong manifest không khớp với phần mềm cần cập nhật");
        if (!_s.Catalog.Packages.Any(p => Same(p.Id, id)))
            throw new Errors.UserMessageException("Không tìm thấy phần mềm");

        if ((keepOld || replaceVersion != null) && _s.Catalog.Packages.Any(p => Same(p.Id, id) && Same(p.Version, m.Version)
                && !(replaceVersion != null && !keepOld && Same(p.Version, replaceVersion))))
            throw new Errors.UserMessageException($"Phần mềm {m.Id} phiên bản {m.Version} đã tồn tại");

        var entry = PackageBuilder.Build(sourceFolder, _s.Store.Root, _iconFromExe);
        var removed = _s.Catalog.Packages.Where(p => Same(p.Id, id) && (keepOld ? Same(p.Version, m.Version)
            : replaceVersion == null || Same(p.Version, replaceVersion))).ToList();
        var packages = _s.Catalog.Packages.Except(removed).Append(entry).ToList();
        try { Commit(packages); }
        catch (Exception ex) when (ex is not CatalogCommitPendingException) { DeleteFiles(entry); throw; }
        foreach (var old in removed) DeleteFiles(old);
        return entry;
    }

    /// <summary>Rewrites only the metadata of one stored package and re-signs the catalog; blob, hash and keys are kept.</summary>
    public PackageEntry UpdatePackageInfo(string id, string version, PackageEntry updated)
    {
        var old = _s.Catalog.Packages.FirstOrDefault(p => Same(p.Id, id) && Same(p.Version, version))
                  ?? throw new Errors.UserMessageException("Không tìm thấy phần mềm");
        if (!Same(updated.Id, id)) throw new InvalidDataException("Trường id không khớp với phần mềm cần cập nhật");
        if (!System.Text.RegularExpressions.Regex.IsMatch(updated.Version ?? "", @"^[A-Za-z0-9._-]+\z"))
            throw new InvalidDataException("Trường version chứa ký tự không hợp lệ");
        if (!Same(updated.Version!, version) && _s.Catalog.Packages.Any(p => Same(p.Id, id) && Same(p.Version, updated.Version!)))
            throw new Errors.UserMessageException($"Phần mềm {id} phiên bản {updated.Version} đã tồn tại");
        if (old.InstallerType == InstallerType.Msi && !string.IsNullOrWhiteSpace(updated.SilentArgs) &&
            updated.SilentArgs.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(t => t[0] is '/' or '-'))
            throw new InvalidDataException("silent_args của phần mềm MSI chỉ được chứa thuộc tính dạng TÊN=giá_trị (không dùng /qn, /norestart...)");
        var entry = updated with
        {
            InstallerType = old.InstallerType, InstallerFile = old.InstallerFile, Docs = old.Docs, PreScript = old.PreScript,
            PostScript = old.PostScript, UninstallScript = old.UninstallScript, Extras = old.Extras,
            BlobFile = old.BlobFile, Sha256 = old.Sha256, KeyB64 = old.KeyB64, DocsKeyB64 = old.DocsKeyB64, BlobSize = old.BlobSize
        };
        Commit(_s.Catalog.Packages.Select(p => p == old ? entry : p).ToList());
        return entry;
    }

    /// <summary>Edit with file changes: unpacks the stored package into a protected workspace, merges the draft over it and re-packs.</summary>
    public async Task<PackageEntry> UpdatePackageFilesAsync(PackageDraft d, bool keepOld, Func<Install.TempWorkspace> workspaceFactory,
        CancellationToken ct = default)
    {
        var edit = d.Edit ?? throw new ArgumentException("Thiếu thông tin sửa", nameof(d));
        var ex = _s.Catalog.Packages.FirstOrDefault(p => Same(p.Id, edit.Existing.Id) && Same(p.Version, edit.Existing.Version))
                 ?? throw new Errors.UserMessageException("Không tìm thấy phần mềm");
        var k = edit.Kept;
        using var ws = workspaceFactory();
        var repo = new Repository.PortableRepositorySource(_s, null!); // WriteLog is never called here
        if (!await Install.InstallService.ExtractAsync(repo, ex, ws, new Progress<Install.InstallProgress>(), ct).ConfigureAwait(false))
            throw new InvalidDataException(Strings.PackageCorrupt);
        var root = Path.GetFullPath(Path.Combine(ws.Path, "pkg"));

        string Under(string sub, string rel)
        {
            var dir = Path.GetFullPath(Path.Combine(root, sub)) + Path.DirectorySeparatorChar;
            var p = Path.GetFullPath(Path.Combine(dir, rel));
            if (!p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || !File.Exists(p) || (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Không tìm thấy file trong gói: {sub}\\{rel}");
            return p;
        }
        var oldSub = ex.InstallerType == InstallerType.Script ? "scripts" : "installer";
        var installer = d.InstallerPath ?? (k.Installer is { } i ? Under(oldSub, i) : throw new InvalidDataException("Chọn file cài"));
        var entries = (k.Extras ?? PackageBuilder.ExtraNames(root, ex))
            .Select(r => new ExtraFile(File.Exists(Path.Combine(root, oldSub, r)) ? Under(oldSub, r) : Under("extras", r), r))
            .Concat(d.ExtraEntries ?? new()).ToList();
        var docsKey = Convert.FromBase64String(ex.DocsKeyB64 ?? ex.KeyB64);
        var keptDocs = new List<string>();
        foreach (var name in k.Docs)
        {
            await using var src = await repo.OpenDocBlobAsync(ex, name, ct).ConfigureAwait(false); // validates the name
            var path = Path.Combine(ws.Path, "docs", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                Crypto.PackageCrypto.Decrypt(src, dst, docsKey);
            keptDocs.Add(path);
        }
        var resolved = d with
        {
            InstallerPath = installer, ExtraEntries = entries, DocFiles = keptDocs.Concat(d.DocFiles).ToList(),
            PreScriptPath = d.PreScriptPath ?? (k.PreScript is { } a ? Under("scripts", a) : null),
            PostScriptPath = d.PostScriptPath ?? (k.PostScript is { } b ? Under("scripts", b) : null),
            UninstallScriptPath = d.UninstallScriptPath ?? (k.UninstallScript is { } c ? Under("scripts", c) : null),
            Edit = null
        };
        var dir = PackageSource.Create(resolved, Path.Combine(ws.Path, "src"));
        return UpdatePackage(ex.Id, dir, keepOld, ex.Version);
    }

    public void RemovePackage(string id, string? version)
    {
        var removed = _s.Catalog.Packages
            .Where(p => Same(p.Id, id) && (version is null || Same(p.Version, version))).ToList();
        if (removed.Count == 0) throw new Errors.UserMessageException("Không tìm thấy phần mềm");
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
