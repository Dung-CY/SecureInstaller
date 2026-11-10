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
    public void AddPackage_writes_blob_docs_and_bumps_version()
    {
        var (root, s, a) = NewVault();
        var e = a.AddPackage(SamplePackage.Create(_tmp));
        Assert.Equal(1, s.Catalog.Version);
        Assert.Equal("demo-1.0.pkg.enc", e.BlobFile);
        Assert.True(File.Exists(Blob(root, e)));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Blob(root, e)))).ToLowerInvariant(), e.Sha256);
        Assert.True(File.Exists(Path.Combine(root, "vault", "docs", "demo-1.0", "HuongDan.docx.enc")));
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
        Assert.Throws<InvalidOperationException>(() => a.AddPackage(SamplePackage.Create(_tmp)));
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
        Assert.False(Directory.Exists(Path.Combine(root, "vault", "docs", "demo-1.0")));
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
        Assert.False(Directory.Exists(Path.Combine(root, "vault", "docs", "demo-1.0")));
        Assert.Empty(s.Catalog.Packages);
        Assert.Equal(2, s.Catalog.Version);
        Assert.Throws<InvalidOperationException>(() => a.RemovePackage("demo", null));
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
        Assert.Throws<InvalidOperationException>(() => a.AddUser("BOB", "x", UserRole.User));
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
        var s = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        Assert.Single(s.Catalog.Packages);
    }

    [Fact]
    public void Cannot_remove_last_admin()
    {
        var (root, _, a) = NewVault();
        a.AddUser("bob", "pw-b", UserRole.User);
        Assert.Throws<InvalidOperationException>(() => a.RemoveUser("admin"));
        a.RemoveUser("bob");
        Assert.Single(VaultStore.Open(root).Meta.Users);
    }
}
