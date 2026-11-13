using System.Text.Json;
using Installer.Core.Model;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class FormatCompatTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"));
    public FormatCompatTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Format1_catalog_json_without_new_fields_deserializes_with_defaults()
    {
        const string json = """
        {
          "version": 3, "updated_at": "2026-11-10T09:00:00+00:00",
          "packages": [{
            "id": "demo", "name": "Demo", "version": "1.0", "installer_type": "exe", "installer_file": "setup.exe",
            "silent_args": "/S", "target_dir": null, "timeout_sec": 1800,
            "detect": { "product_code": null, "display_name_regex": null },
            "docs": ["a.docx"], "post_actions": [], "rules": [],
            "blob_file": "demo-1.0-aabbccdd.pkg.enc", "sha256": "00", "key_b64": "AAAA"
          }],
          "keyword_scan": { "keywords": [], "paths": [], "max_seconds": 60 },
          "crack_signatures": []
        }
        """;
        var c = JsonSerializer.Deserialize<Catalog>(json, Json.Options)!;
        var p = Assert.Single(c.Packages);
        Assert.Equal(0, p.BlobSize);
        Assert.Null(p.DocsKeyB64);
        Assert.Null(p.IconPngB64);
        Assert.Null(p.Uninstall);
        Assert.Null(c.Bundles);
        Assert.Equal(1, c.Format);
    }

    [Fact]
    public void New_package_has_separate_docs_key_and_blob_size()
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        var s = VaultStore.Open(root).Unlock("admin", "pw-1", DateTimeOffset.UtcNow);
        var e = new VaultAdmin(s).AddPackage(SamplePackage.Create(_tmp));
        Assert.False(string.IsNullOrEmpty(e.DocsKeyB64));
        Assert.NotEqual(e.KeyB64, e.DocsKeyB64);
        Assert.Equal(new FileInfo(Path.Combine(root, "vault", "packages", e.BlobFile)).Length, e.BlobSize);
        Assert.Equal(2, s.Catalog.Format);
        var s2 = VaultStore.Open(root).Unlock("admin", "pw-1", DateTimeOffset.UtcNow);
        Assert.Equal(2, s2.Catalog.Format);
    }

    [Fact]
    public void Manifest_uninstall_command_requires_command()
    {
        string Src(string unins) => SamplePackage.Create(_tmp, manifestOverride: $$"""
            { "id": "u", "name": "U", "version": "1.0", "installer_type": "exe", "installer_file": "setup.exe",
              "silent_args": "/S", "uninstall": {{unins}} }
            """);
        var ex = Assert.Throws<InvalidDataException>(() => PackageBuilder.ReadManifest(Src("""{ "type": "command" }""")));
        Assert.Contains("command", ex.Message);
        Assert.Throws<InvalidDataException>(() => PackageBuilder.ReadManifest(Src("""{ "type": "bogus" }""")));
        var ok = PackageBuilder.ReadManifest(Src("""{ "type": "command", "command": "u.exe", "args": "/x" }"""));
        Assert.Equal(new UninstallSpec("command", "u.exe", "/x"), ok.Entry.Uninstall);
    }
}
