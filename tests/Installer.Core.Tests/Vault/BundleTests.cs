using System.Text;
using Installer.Core.Model;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;
using Installer.Packager;

namespace Installer.Core.Tests.Vault;

public class BundleTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_bundle_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public BundleTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static PackageEntry Pkg(string id, string ver) => new(id, id, ver, InstallerType.Exe, "setup.exe", "/S", null, 60, new DetectRule(null, null), new(), new(), new(), "b", "s", "k");

    static Catalog Cat(params PackageEntry[] p) => Catalog.Empty() with { Packages = p.ToList() };

    [Fact]
    public void Resolve_latest_and_exact_version()
    {
        var c = Cat(Pkg("a", "1.2"), Pkg("a", "1.10"), Pkg("A", "1.9"), Pkg("b", "2.0"));
        var b = new Bundle("k", "K", "", new() { new("a"), new("B", "2.0"), new("a", "1.2") });
        var r = BundleResolver.Resolve(c, b);
        Assert.Equal("1.10", r[0].Package!.Version);
        Assert.Equal("b", r[1].Package!.Id);
        Assert.Equal("1.2", r[2].Package!.Version);
        Assert.Equal(new[] { "a", "B", "a" }, r.Select(x => x.Item.PackageId));
    }

    [Fact]
    public void Resolve_missing_returns_null_entry()
    {
        var c = Cat(Pkg("a", "1.0"));
        var r = BundleResolver.Resolve(c, new Bundle("k", "K", "", new() { new("zzz"), new("a", "9.9") }));
        Assert.Null(r[0].Package);
        Assert.Null(r[1].Package);
    }

    (string root, VaultSession s, VaultAdmin a) NewVault()
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        var s = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        var a = new VaultAdmin(s);
        a.AddPackage(SamplePackage.Create(_tmp));
        return (root, s, a);
    }

    [Fact]
    public void AddBundle_rejects_unknown_package()
    {
        var (_, s, a) = NewVault();
        var v = s.Catalog.Version;
        var ex = Assert.Throws<InvalidDataException>(() =>
            a.AddBundle(new Bundle("k", "K", "", new() { new("demo"), new("ghost") })));
        Assert.Equal("Bộ gói tham chiếu gói không có trong kho: ghost", ex.Message);
        Assert.Throws<InvalidDataException>(() => a.AddBundle(new Bundle("bad id", "K", "", new() { new("demo") })));
        Assert.Throws<InvalidDataException>(() => a.AddBundle(new Bundle("k", " ", "", new() { new("demo") })));
        Assert.Throws<InvalidDataException>(() => a.AddBundle(new Bundle("k", "K", "", new())));
        Assert.Equal(v, s.Catalog.Version);
    }

    [Fact]
    public void Update_and_remove_bundle()
    {
        var (_, s, a) = NewVault();
        var v = s.Catalog.Version;
        a.AddBundle(new Bundle("k", "K", "d", new() { new("demo") }));
        Assert.Equal(v + 1, s.Catalog.Version);
        var ex = Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.AddBundle(new Bundle("K", "K2", "", new() { new("demo") })));
        Assert.Equal("Bộ gói đã tồn tại", ex.Message);
        a.UpdateBundle(new Bundle("k", "K2", "d", new() { new("demo", "1.0") }));
        Assert.Equal("K2", Assert.Single(s.Catalog.Bundles!).Name);
        Assert.Equal(v + 2, s.Catalog.Version);
        Assert.Equal("Không tìm thấy bộ gói",
            Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.UpdateBundle(new Bundle("zz", "Z", "", new() { new("demo") }))).Message);
        a.RemoveBundle("k");
        Assert.Empty(s.Catalog.Bundles!);
        Assert.Throws<Installer.Core.Errors.UserMessageException>(() => a.RemoveBundle("k"));
    }

    [Fact]
    public void RemovePackage_does_not_cascade_into_bundles()
    {
        var (_, s, a) = NewVault();
        a.AddBundle(new Bundle("k", "K", "", new() { new("demo") }));
        a.RemovePackage("demo", null);
        Assert.Single(s.Catalog.Bundles!);
    }

    [Fact]
    public void Bundle_survives_reopen()
    {
        var (root, _, a) = NewVault();
        a.AddBundle(new Bundle("k", "K", "d", new() { new("demo", "1.0") }));
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        var b = Assert.Single(s2.Catalog.Bundles!);
        Assert.Equal("d", b.Description);
        Assert.Equal("1.0", b.Items[0].Version);
    }

    [Fact]
    public void Cli_add_bundle_returns_0()
    {
        var root = Path.Combine(_tmp, "kho");
        Assert.Equal(0, Program.Run(new[] { "init", root, "--user", "admin", "--password", "pw-123456" }));
        var src = SamplePackage.Create(_tmp);
        Assert.Equal(0, Program.Run(new[] { "add-package", root, src, "--user", "admin", "--password", "pw-123456" }));
        var f = Path.Combine(_tmp, "bundle.json");
        File.WriteAllText(f, """{ "id": "ke-toan", "name": "Kế toán", "description": "", "items": [ { "package_id": "demo" } ] }""", new UTF8Encoding(false));
        var auth = new[] { "--user", "admin", "--password", "pw-123456" };
        Assert.Equal(0, Program.Run(new[] { "add-bundle", root, f }.Concat(auth).ToArray()));
        Assert.Equal(0, Program.Run(new[] { "add-bundle", root, f }.Concat(auth).ToArray()));
        Assert.Equal(1, Program.Run(new[] { "remove-bundle", root, "nope" }.Concat(auth).ToArray()));
        Assert.Equal(0, Program.Run(new[] { "remove-bundle", root, "ke-toan" }.Concat(auth).ToArray()));
    }
}
