using System.Security.Cryptography;
using System.Text.Json;
using Installer.Core.Crypto;
using Installer.Core.Model;

namespace Installer.Core.Vault;

public sealed class VaultStore
{
    const int MaxFailures = 5;
    static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public string Root { get; }
    public bool IsReadOnly { get; }
    public VaultMeta Meta { get; private set; }

    string VaultDir => Path.Combine(Root, "vault");
    string MetaPath => Path.Combine(VaultDir, "vault.meta");
    string CatalogPath => Path.Combine(VaultDir, "catalog.json.enc");
    string SigPath => Path.Combine(VaultDir, "catalog.sig");

    VaultStore(string root, VaultMeta meta)
    {
        Root = root;
        Meta = meta;
        IsReadOnly = ProbeReadOnly(Path.Combine(root, "vault"));
    }

    static bool ProbeReadOnly(string vaultDir)
    {
        try
        {
            var probe = Path.Combine(vaultDir, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return false;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>Short human-comparable fingerprint of the signing public key (SHA-256 of SPKI, first 8 bytes, 4-4-4-4).</summary>
    public static string Fingerprint(string signingPublicKeyB64)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(signingPublicKeyB64)))[..16];
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public static VaultStore Create(string root, string adminUser, string adminPassword)
    {
        var vaultDir = Path.Combine(root, "vault");
        if (Directory.Exists(vaultDir)) throw new IOException($"Vault already exists: {vaultDir}");
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(Path.Combine(vaultDir, "packages"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "docs"));

        var vk = KeyDerivation.NewKey();
        var (priv, pub) = CatalogSigner.NewKeyPair();
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek(adminPassword, salt);
        var admin = new UserEntry(adminUser, UserRole.Admin, Convert.ToBase64String(salt),
            Convert.ToBase64String(KeyWrap.Wrap(vk, kek)), Convert.ToBase64String(KeyWrap.Wrap(priv, kek)), 0, null);
        var meta = new VaultMeta(1, Guid.NewGuid(), Convert.ToBase64String(pub), new List<UserEntry> { admin });

        WriteCatalog(vaultDir, Catalog.Empty(), vk, priv);

        var store = new VaultStore(root, meta);
        store.SaveMeta(meta);
        return store;
    }

    public static VaultStore Open(string root)
    {
        var metaPath = Path.Combine(root, "vault", "vault.meta");
        if (!File.Exists(metaPath)) throw new VaultNotFoundException(Strings.VaultNotFound);
        VaultMeta? meta;
        try { meta = JsonSerializer.Deserialize<VaultMeta>(File.ReadAllBytes(metaPath), Json.Options); }
        catch (JsonException) { throw new VaultTamperedException(Strings.VaultTampered); }
        if (meta is null || meta.Format != 1 || meta.Users is null)
            throw new VaultTamperedException(Strings.VaultTampered);
        var store = new VaultStore(root, meta);
        if (!store.IsReadOnly) store.RepairCatalogTmp();
        return store;
    }

    void RepairCatalogTmp()
    {
        var encTmp = CatalogPath + ".tmp";
        var sigTmp = SigPath + ".tmp";
        try
        {
            if (!File.Exists(encTmp) && !File.Exists(sigTmp)) return;
            var pub = Convert.FromBase64String(Meta.SigningPublicKeyB64);
            byte[]? liveEnc = File.Exists(CatalogPath) ? File.ReadAllBytes(CatalogPath) : null;
            byte[]? liveSig = File.Exists(SigPath) ? File.ReadAllBytes(SigPath) : null;
            if (liveEnc != null && liveSig != null && CatalogSigner.Verify(liveEnc, liveSig, pub))
            {
                File.Delete(encTmp);
                File.Delete(sigTmp);
                return;
            }
            var sigTmpBytes = File.Exists(sigTmp) ? File.ReadAllBytes(sigTmp) : null;
            if (liveEnc != null && sigTmpBytes != null && CatalogSigner.Verify(liveEnc, sigTmpBytes, pub))
            {
                File.Move(sigTmp, SigPath, overwrite: true);
                File.Delete(encTmp);
                return;
            }
            if (sigTmpBytes != null && File.Exists(encTmp) && CatalogSigner.Verify(File.ReadAllBytes(encTmp), sigTmpBytes, pub))
            {
                File.Move(encTmp, CatalogPath, overwrite: true);
                File.Move(sigTmp, SigPath, overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
    }

    public VaultSession Unlock(string username, string password, DateTimeOffset now)
    {
        var user = Meta.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
                   ?? throw new BadCredentialsException(Strings.BadCredentials);

        if (user.LockedUntil is { } until)
        {
            if (until > now)
                throw new AccountLockedException(until);
            user = user with { FailedAttempts = 0, LockedUntil = null };
            Replace(user);
        }

        var kek = KeyDerivation.DeriveKek(password, Convert.FromBase64String(user.SaltB64));
        byte[] vk;
        try { vk = KeyWrap.Unwrap(Convert.FromBase64String(user.WrappedVkB64), kek); }
        catch (BadCredentialsException)
        {
            var failed = user.FailedAttempts + 1;
            user = user with
            {
                FailedAttempts = failed,
                LockedUntil = failed >= MaxFailures ? now + LockDuration : null
            };
            Replace(user);
            throw new BadCredentialsException(Strings.BadCredentials);
        }

        if (user.FailedAttempts != 0 || user.LockedUntil != null)
            Replace(user with { FailedAttempts = 0, LockedUntil = null });

        byte[] catalogEnc, sig;
        try
        {
            catalogEnc = File.ReadAllBytes(CatalogPath);
            sig = File.ReadAllBytes(SigPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new VaultTamperedException(Strings.VaultTampered);
        }

        if (!CatalogSigner.Verify(catalogEnc, sig, Convert.FromBase64String(Meta.SigningPublicKeyB64)))
            throw new VaultTamperedException(Strings.VaultTampered);

        Catalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<Catalog>(PackageCrypto.DecryptBytes(catalogEnc, vk), Json.Options);
        }
        catch (Exception e) when (e is PackageCorruptException or JsonException)
        {
            throw new VaultTamperedException(Strings.VaultTampered);
        }
        if (catalog is null) throw new VaultTamperedException(Strings.VaultTampered);
        VerifyBlobs(catalog);

        byte[]? signingKey = null;
        if (user.WrappedSigningKeyB64 is { } w)
        {
            try
            {
                var candidate = KeyWrap.Unwrap(Convert.FromBase64String(w), kek);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportPkcs8PrivateKey(candidate, out _);
                var spki = ecdsa.ExportSubjectPublicKeyInfo();
                if (CryptographicOperations.FixedTimeEquals(spki, Convert.FromBase64String(Meta.SigningPublicKeyB64)))
                    signingKey = candidate;
            }
            catch (Exception e) when (e is BadCredentialsException or CryptographicException or FormatException)
            {
                signingKey = null;
            }
        }

        return new VaultSession(user.Username, signingKey != null, vk, signingKey, catalog, this);
    }

    void VerifyBlobs(Catalog catalog)
    {
        var dir = Path.Combine(VaultDir, "packages");
        foreach (var p in catalog.Packages)
        {
            long len;
            try
            {
                var fi = new FileInfo(Path.Combine(dir, p.BlobFile));
                if (!fi.Exists) throw new VaultTamperedException(Strings.VaultTampered);
                len = fi.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new VaultTamperedException(Strings.VaultTampered);
            }
            if (p.BlobSize > 0 && p.BlobSize != len) throw new VaultTamperedException(Strings.VaultTampered);
        }
    }

    void Replace(UserEntry updated)
    {
        var users = Meta.Users.Select(u =>
            string.Equals(u.Username, updated.Username, StringComparison.OrdinalIgnoreCase) ? updated : u).ToList();
        var meta = Meta with { Users = users };
        Meta = meta;
        if (IsReadOnly) return;
        try { SaveMeta(meta); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal void WriteCatalog(Catalog catalog, byte[] vk, byte[] signingKey) =>
        WriteCatalog(VaultDir, catalog, vk, signingKey);

    static void WriteCatalog(string vaultDir, Catalog catalog, byte[] vk, byte[] signingKey)
    {
        var catalogEnc = PackageCrypto.EncryptBytes(JsonSerializer.SerializeToUtf8Bytes(catalog, Json.Options), vk);
        var sig = CatalogSigner.Sign(catalogEnc, signingKey);
        var encPath = Path.Combine(vaultDir, "catalog.json.enc");
        var sigPath = Path.Combine(vaultDir, "catalog.sig");
        try
        {
            WriteDurable(encPath + ".tmp", catalogEnc);
            WriteDurable(sigPath + ".tmp", sig);
            File.Move(encPath + ".tmp", encPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(encPath + ".tmp"); } catch { }
            try { File.Delete(sigPath + ".tmp"); } catch { }
            throw;
        }
        try { File.Move(sigPath + ".tmp", sigPath, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new CatalogCommitPendingException(e);
        }
    }

    static void WriteDurable(string path, byte[] bytes)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(bytes);
        fs.Flush(true);
    }

    internal void UpdateMeta(VaultMeta meta)
    {
        SaveMeta(meta);
    }

    internal void SaveMeta(VaultMeta meta)
    {
        var tmp = MetaPath + ".tmp";
        WriteDurable(tmp, JsonSerializer.SerializeToUtf8Bytes(meta, Json.Options));
        File.Move(tmp, MetaPath, overwrite: true);
        Meta = meta;
    }
}
