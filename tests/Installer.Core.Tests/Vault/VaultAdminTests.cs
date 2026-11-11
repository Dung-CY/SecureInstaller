using System.Security.Cryptography;
using Installer.Core.Model;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class VaultAdminTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public VaultAdminTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    (string root, VaultSession s, VaultAdmin a) NewVault()
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        var s = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        return (root, s, new VaultAdmin(s));
    }

    string Blob(string root, PackageEntry e) => Path.Combine(root, "vault", "packages", e.BlobFile);

    [Fact]
    public void Msi_silent_args_with_switches_rejected()
    {
        var (_, _, a) = NewVault();
        var src = SamplePackage.Create(_tmp, manifestOverride: """
            { "id": "m", "name": "M", "version": "1.0", "installer_type": "msi", "installer_file": "setup.exe", "silent_args": "/qn /norestart" }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => a.AddPackage(src));
        Assert.Contains("silent_args", ex.Message);
    }

    [Fact]
    public void AddPackage_writes_blob_docs_and_bumps_version()
    {
        var (root, s, a) = NewVault();
        var e = a.AddPackage(SamplePackage.Create(_tmp));
        Assert.Equal(1, s.Catalog.Version);
        Assert.Matches(@"^demo-1\.0-[0-9a-f]{8}\.pkg\.enc$", e.BlobFile);
        Assert.True(File.Exists(Blob(root, e)));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Blob(root, e)))).ToLowerInvariant(), e.Sha256);
        Assert.True(File.Exists(Path.Combine(PackageBuilder.DocsDirFor(e, root), "HuongDan.docx.enc")));
        Assert.Equal(new[] { "HuongDan.docx" }, e.Docs);
        Assert.Equal(1800, e.TimeoutSec);
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        Assert.Equal(1, s2.Catalog.Version);
        Assert.Single(s2.Catalog.Packages);
        Assert.Equal(e.Sha256, s2.Catalog.Packages[0].Sha256);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "vault"), "*.tmp"));
    }

    [Fact]
    public void AddPackage_missing_installer_file_throws_with_field_name()
    {
        var (_, _, a) = NewVault();
        var ex = Assert.Throws<InvalidDataException>(() => a.AddPackage(SamplePackage.Create(_tmp, withInstaller: false)));
        Assert.Contains("installer_file", ex.Message);
        var bad = SamplePackage.Create(_tmp, manifestOverride: "{ \"id\": \"x\", \"name\": \"n\", \"version\": \"1\", \"installer_file\": \"setup.exe\" }");
        Assert.Contains("installer_type", Assert.Throws<InvalidDataException>(() => a.AddPackage(bad)).Message);
    }

    [Fact]
    public void AddPackage_duplicate_id_version_throws()
    {
        var (root, s, a) = NewVault();
        a.AddPackage(SamplePackage.Create(_tmp));
        Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.AddPackage(SamplePackage.Create(_tmp)));
        Assert.Equal(1, s.Catalog.Version);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "vault", "packages")));
    }

    [Fact]
    public void UpdatePackage_keepOld_false_removes_old_blob()
    {
        var (root, s, a) = NewVault();
        var v1 = a.AddPackage(SamplePackage.Create(_tmp, version: "1.0"));
        var v2 = a.UpdatePackage("demo", SamplePackage.Create(_tmp, version: "2.0"), keepOld: false);
        Assert.False(File.Exists(Blob(root, v1)));
        Assert.True(File.Exists(Blob(root, v2)));
        Assert.False(Directory.Exists(PackageBuilder.DocsDirFor(v1, root)));
        Assert.Equal(new[] { "2.0" }, s.Catalog.Packages.Select(p => p.Version));
    }

    [Fact]
    public void UpdatePackage_keepOld_true_keeps_both_versions()
    {
        var (root, s, a) = NewVault();
        var v1 = a.AddPackage(SamplePackage.Create(_tmp, version: "1.0"));
        var v2 = a.UpdatePackage("demo", SamplePackage.Create(_tmp, version: "2.0"), keepOld: true);
        Assert.True(File.Exists(Blob(root, v1)));
        Assert.True(File.Exists(Blob(root, v2)));
        Assert.Equal(2, s.Catalog.Packages.Count);
    }

    [Fact]
    public void RemovePackage_deletes_blob_and_docs()
    {
        var (root, s, a) = NewVault();
        var e = a.AddPackage(SamplePackage.Create(_tmp));
        a.RemovePackage("demo", null);
        Assert.False(File.Exists(Blob(root, e)));
        Assert.False(Directory.Exists(PackageBuilder.DocsDirFor(e, root)));
        Assert.Empty(s.Catalog.Packages);
        Assert.Equal(2, s.Catalog.Version);
        Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.RemovePackage("demo", null));
    }

    [Fact]
    public void User_role_cannot_construct_VaultAdmin()
    {
        var (root, _, a) = NewVault();
        a.AddUser("bob", "pw-b", UserRole.User);
        var bob = VaultStore.Open(root).Unlock("bob", "pw-b", T0);
        Assert.Throws<NotAdminException>(() => new VaultAdmin(bob));
    }

    [Fact]
    public void Added_user_can_unlock_and_is_not_admin()
    {
        var (root, _, a) = NewVault();
        a.AddUser("bob", "pw-b", UserRole.User);
        Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.AddUser("BOB", "x", UserRole.User));
        var bob = VaultStore.Open(root).Unlock("bob", "pw-b", T0);
        Assert.False(bob.IsAdmin);
        a.SetPassword("bob", "pw-new");
        var store = VaultStore.Open(root);
        Assert.Throws<BadCredentialsException>(() => store.Unlock("bob", "pw-b", T0));
        Assert.False(store.Unlock("bob", "pw-new", T0).IsAdmin);
    }

    [Fact]
    public void Added_admin_can_sign()
    {
        var (root, _, a) = NewVault();
        a.AddUser("boss", "pw-b", UserRole.Admin);
        var boss = VaultStore.Open(root).Unlock("boss", "pw-b", T0);
        Assert.True(boss.IsAdmin);
        new VaultAdmin(boss).AddPackage(SamplePackage.Create(_tmp));
        a.AddUser("bob", "pw-b", UserRole.User);
        var bob = VaultStore.Open(root).Unlock("bob", "pw-b", T0);
        Assert.False(bob.IsAdmin);
        Assert.Single(bob.Catalog.Packages);
    }

    [Fact]
    public void Cannot_remove_last_admin()
    {
        var (root, _, a) = NewVault();
        a.AddUser("bob", "pw-b", UserRole.User);
        Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.RemoveUser("admin"));
        a.RemoveUser("bob");
        Assert.Single(VaultStore.Open(root).Meta.Users);
    }

    bool HashOk(string root, PackageEntry e) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Blob(root, e)))).ToLowerInvariant() == e.Sha256;

    [Fact]
    public void Same_version_update_keeps_working_package_and_cleans_old_files()
    {
        var (root, s, a) = NewVault();
        var v1 = a.AddPackage(SamplePackage.Create(_tmp));
        var v1b = a.UpdatePackage("demo", SamplePackage.Create(_tmp), keepOld: false);
        Assert.NotEqual(v1.BlobFile, v1b.BlobFile);
        Assert.False(File.Exists(Blob(root, v1)));
        Assert.False(Directory.Exists(PackageBuilder.DocsDirFor(v1, root)));
        Assert.True(HashOk(root, v1b));
        Assert.Single(s.Catalog.Packages);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "vault", "packages")));
    }

    [Fact]
    public void Colliding_ids_do_not_overwrite_each_other()
    {
        var (root, s, a) = NewVault();
        var p1 = a.AddPackage(SamplePackage.Create(_tmp, id: "a-b", version: "c"));
        var p2 = a.AddPackage(SamplePackage.Create(_tmp, id: "a", version: "b-c"));
        Assert.NotEqual(p1.BlobFile, p2.BlobFile);
        Assert.True(HashOk(root, p1));
        Assert.True(HashOk(root, p2));
        a.RemovePackage("a", null);
        Assert.True(HashOk(root, p1));
        Assert.True(Directory.Exists(PackageBuilder.DocsDirFor(p1, root)));
    }

    [Fact]
    public void Failed_build_leaves_existing_package_intact()
    {
        var (root, s, a) = NewVault();
        var v1 = a.AddPackage(SamplePackage.Create(_tmp));
        Assert.Throws<InvalidDataException>(() =>
            a.UpdatePackage("demo", SamplePackage.Create(_tmp, withInstaller: false), keepOld: false));
        Assert.True(HashOk(root, v1));
        Assert.True(Directory.Exists(PackageBuilder.DocsDirFor(v1, root)));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "vault", "packages")));
        Assert.Equal(1, s.Catalog.Version);
    }

    (byte[] enc, byte[] sig) NewerCatalog(VaultSession s)
    {
        var newer = s.Catalog with { Version = s.Catalog.Version + 1 };
        var enc = Installer.Core.Crypto.PackageCrypto.EncryptBytes(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(newer, Json.Options), s.VaultKey);
        return (enc, Installer.Core.Crypto.CatalogSigner.Sign(enc, s.SigningPrivateKey!));
    }

    [Fact]
    public void Open_completes_commit_when_crash_between_moves()
    {
        var (root, s, a) = NewVault();
        a.AddPackage(SamplePackage.Create(_tmp));
        var (enc, sig) = NewerCatalog(s);
        var dir = Path.Combine(root, "vault");
        File.WriteAllBytes(Path.Combine(dir, "catalog.json.enc"), enc);   // live enc = new, live sig = old
        File.WriteAllBytes(Path.Combine(dir, "catalog.sig.tmp"), sig);
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        Assert.Equal(2, s2.Catalog.Version);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Open_discards_tmp_pair_when_live_pair_valid()
    {
        var (root, s, a) = NewVault();
        a.AddPackage(SamplePackage.Create(_tmp));
        var (enc, sig) = NewerCatalog(s);
        var dir = Path.Combine(root, "vault");
        File.WriteAllBytes(Path.Combine(dir, "catalog.json.enc.tmp"), enc);
        File.WriteAllBytes(Path.Combine(dir, "catalog.sig.tmp"), sig);
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        Assert.Equal(1, s2.Catalog.Version);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Commit_pending_keeps_new_blob()
    {
        var (root, s, a) = NewVault();
        var sigPath = Path.Combine(root, "vault", "catalog.sig");
        File.SetAttributes(sigPath, FileAttributes.ReadOnly);
        try
        {
            var src = SamplePackage.Create(_tmp);
            Assert.Throws<CatalogCommitPendingException>(() => a.AddPackage(src));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "vault", "packages")));
        }
        finally { File.SetAttributes(sigPath, FileAttributes.Normal); }
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        var e = Assert.Single(s2.Catalog.Packages);
        Assert.True(HashOk(root, e));
    }

    [Fact]
    public void Open_discards_invalid_tmp_pair()
    {
        var (root, s, a) = NewVault();
        a.AddPackage(SamplePackage.Create(_tmp));
        var dir = Path.Combine(root, "vault");
        File.WriteAllBytes(Path.Combine(dir, "catalog.json.enc.tmp"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(dir, "catalog.sig.tmp"), new byte[64]);
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        Assert.Equal(1, s2.Catalog.Version);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
