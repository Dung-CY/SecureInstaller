using System.Text.Json;
using Installer.Core.Crypto;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class VaultStoreTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public VaultStoreTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string NewRoot(string name = "kho") => Path.Combine(_tmp, name);

    [Fact]
    public void Create_then_Unlock_admin_IsAdmin_true_catalog_version_0()
    {
        var root = NewRoot("Dự án 1");
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        var s = store.Unlock("ADMIN", "pw-1", T0);
        Assert.True(s.IsAdmin);
        Assert.NotNull(s.SigningPrivateKey);
        Assert.Equal(0, s.Catalog.Version);
        Assert.Empty(s.Catalog.Packages);
        Assert.Equal(60, s.Catalog.KeywordScan.MaxSeconds);
        Assert.Throws<IOException>(() => VaultStore.Create(root, "admin", "pw-1"));
    }

    [Fact]
    public void Open_missing_vault_throws_VaultNotFound()
    {
        Assert.Throws<VaultNotFoundException>(() => VaultStore.Open(NewRoot("none")));
    }

    [Fact]
    public void Wrong_password_throws_BadCredentials_and_increments_counter()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        var ex = Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0));
        Assert.Equal(Strings.BadCredentials, ex.Message);
        Assert.Equal(1, store.Meta.Users[0].FailedAttempts);
        Assert.Equal(1, VaultStore.Open(root).Meta.Users[0].FailedAttempts);
        Assert.Throws<BadCredentialsException>(() => store.Unlock("nobody", "x", T0));
        Assert.Equal(1, store.Meta.Users[0].FailedAttempts);
    }

    [Fact]
    public void Fifth_failure_locks_for_15_minutes()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        var t5 = T0.AddSeconds(4);
        for (int i = 0; i < 5; i++)
            Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0.AddSeconds(i)));
        var ex = Assert.Throws<AccountLockedException>(() => store.Unlock("admin", "pw-1", t5.AddMinutes(1)));
        Assert.Equal(t5.AddMinutes(15), ex.Until);
    }

    [Fact]
    public void Lock_expires_after_15_minutes()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        for (int i = 0; i < 5; i++)
            Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0));
        var s = store.Unlock("admin", "pw-1", T0.AddMinutes(16));
        Assert.True(s.IsAdmin);
        Assert.Equal(0, store.Meta.Users[0].FailedAttempts);
        Assert.Null(store.Meta.Users[0].LockedUntil);
        Assert.Equal(0, VaultStore.Open(root).Meta.Users[0].FailedAttempts);
    }

    [Fact]
    public void Tampered_catalog_throws_VaultTampered()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var path = Path.Combine(root, "vault", "catalog.json.enc");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        var store = VaultStore.Open(root);
        Assert.Throws<VaultTamperedException>(() => store.Unlock("admin", "pw-1", T0));
    }

    [Fact]
    public void Editing_role_in_meta_does_not_grant_admin()
    {
        var root = NewRoot();
        var store = VaultStore.Create(root, "admin", "pw-1");
        var adminSession = store.Unlock("admin", "pw-1", T0);
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek("pw-u", salt);
        var user = new UserEntry("user1", UserRole.User, Convert.ToBase64String(salt),
            Convert.ToBase64String(KeyWrap.Wrap(adminSession.VaultKey, kek)), null, 0, null);
        store.SaveMeta(store.Meta with { Users = store.Meta.Users.Append(user).ToList() });

        var metaPath = Path.Combine(root, "vault", "vault.meta");
        var text = File.ReadAllText(metaPath);
        Assert.Contains("\"role\": \"user\"", text);
        File.WriteAllText(metaPath, text.Replace("\"role\": \"user\"", "\"role\": \"admin\""));

        var reopened = VaultStore.Open(root);
        Assert.Equal(UserRole.Admin, reopened.Meta.Users[1].Role);
        var s = reopened.Unlock("user1", "pw-u", T0);
        Assert.False(s.IsAdmin);
        Assert.Null(s.SigningPrivateKey);
    }

    [Fact]
    public void Vault_copied_to_vietnamese_path_still_unlocks()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var dest = Path.Combine(_tmp, "Dự án 1", "copy");
        CopyDir(root, dest);
        var s = VaultStore.Open(dest).Unlock("admin", "pw-1", T0);
        Assert.True(s.IsAdmin);
    }

    [Fact]
    public void Json_uses_snake_case()
    {
        var p = new PackageEntry("id", "Name", "1.0", InstallerType.Msi, "a.msi", "/qn", null, 600,
            new DetectRule(null, null), new(), new(), new(), "id-1.0.pkg.enc", "00", "AA==");
        var json = JsonSerializer.Serialize(p, Json.Options);
        Assert.Contains("\"silent_args\"", json);
        Assert.Contains("\"installer_type\": \"msi\"", json);
    }

    [Fact]
    public void Forged_signing_key_under_own_kek_does_not_grant_admin()
    {
        var root = NewRoot();
        var store = VaultStore.Create(root, "admin", "pw-1");
        var vk = store.Unlock("admin", "pw-1", T0).VaultKey;
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek("pw-u", salt);
        string Forge(byte[] secret) => Convert.ToBase64String(KeyWrap.Wrap(secret, kek));
        UserEntry U(string name, byte[] secret) => new(name, UserRole.User, Convert.ToBase64String(salt),
            Convert.ToBase64String(KeyWrap.Wrap(vk, kek)), Forge(secret), 0, null);
        store.SaveMeta(store.Meta with { Users = store.Meta.Users
            .Append(U("junk", KeyDerivation.NewKey()))
            .Append(U("otherkey", CatalogSigner.NewKeyPair().PrivatePkcs8)).ToList() });
        var reopened = VaultStore.Open(root);
        foreach (var name in new[] { "junk", "otherkey" })
        {
            var s = reopened.Unlock(name, "pw-u", T0);
            Assert.False(s.IsAdmin);
            Assert.Null(s.SigningPrivateKey);
        }
    }

    [Fact]
    public void Reencrypted_catalog_with_old_signature_throws_VaultTampered()
    {
        var root = NewRoot();
        var store = VaultStore.Create(root, "admin", "pw-1");
        var session = store.Unlock("admin", "pw-1", T0);
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKek("pw-u", salt);
        store.SaveMeta(store.Meta with { Users = store.Meta.Users.Append(new UserEntry("user1", UserRole.User,
            Convert.ToBase64String(salt), Convert.ToBase64String(KeyWrap.Wrap(session.VaultKey, kek)), null, 0, null)).ToList() });
        var user = VaultStore.Open(root).Unlock("user1", "pw-u", T0);
        var modified = user.Catalog with { Version = 99 };
        File.WriteAllBytes(Path.Combine(root, "vault", "catalog.json.enc"),
            PackageCrypto.EncryptBytes(JsonSerializer.SerializeToUtf8Bytes(modified, Json.Options), user.VaultKey));
        Assert.Throws<VaultTamperedException>(() => VaultStore.Open(root).Unlock("user1", "pw-u", T0));
    }

    [Fact]
    public void Meta_write_failure_still_returns_bad_credentials()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        var metaPath = Path.Combine(root, "vault", "vault.meta");
        File.SetAttributes(metaPath, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0));
            Assert.Equal(1, store.Meta.Users[0].FailedAttempts);
        }
        finally { File.SetAttributes(metaPath, FileAttributes.Normal); }
    }

    [Fact]
    public void Wrong_password_after_lock_expiry_counts_from_one()
    {
        var root = NewRoot();
        VaultStore.Create(root, "admin", "pw-1");
        var store = VaultStore.Open(root);
        for (int i = 0; i < 5; i++)
            Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0));
        Assert.Throws<BadCredentialsException>(() => store.Unlock("admin", "bad", T0.AddMinutes(16)));
        Assert.Equal(1, store.Meta.Users[0].FailedAttempts);
        Assert.Null(store.Meta.Users[0].LockedUntil);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
