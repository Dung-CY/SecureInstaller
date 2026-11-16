using System.IO.Compression;
using Installer.Core.Crypto;
using Installer.Core.Install;
using Installer.Core.Model;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class EditPackageTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_edit_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public EditPackageTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    (string root, VaultSession s, VaultAdmin a) NewVault()
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        var s = VaultStore.Open(root).Unlock("admin", "pw-1", T0);
        return (root, s, new VaultAdmin(s));
    }

    TempWorkspace Ws() => TempWorkspace.Create(Path.Combine(_tmp, "work"), () => false);

    PackageEntry AddWithExtras(VaultAdmin a)
    {
        var src = SamplePackage.Create(_tmp);
        File.WriteAllText(Path.Combine(src, "installer", "b.txt"), "old-b");
        return a.AddPackage(src);
    }

    PackageDraft Draft(PackageEntry e, KeptFiles kept, string? installer = null, List<string>? extras = null,
        List<ExtraFile>? entries = null) =>
        new(e.Id, e.Name, e.Version, e.InstallerType, installer, e.SilentArgs, e.TargetDir, e.TimeoutSec, e.Detect,
            extras ?? new(), new(), null, null, null, 600, new(), null, null, e.IconPngB64, e.Kind, null, entries,
            new PackageEdit(e, kept, true));

    Dictionary<string, string> Unpack(string root, PackageEntry e)
    {
        var zip = Path.Combine(_tmp, "u_" + Guid.NewGuid().ToString("N") + ".zip");
        using (var c = File.OpenRead(Path.Combine(root, "vault", "packages", e.BlobFile)))
        using (var p = File.Create(zip))
            PackageCrypto.Decrypt(c, p, Convert.FromBase64String(e.KeyB64));
        using var za = ZipFile.OpenRead(zip);
        return za.Entries.Where(x => x.Length > 0 || !x.FullName.EndsWith('/')).ToDictionary(x => x.FullName, x =>
        {
            using var r = new StreamReader(x.Open());
            return r.ReadToEnd();
        });
    }

    [Fact]
    public void Metadata_only_edit_keeps_blob_and_changes_args()
    {
        var (_, s, a) = NewVault();
        var e = AddWithExtras(a);
        var v = s.Catalog.Version;
        var u = a.UpdatePackageInfo(e.Id, e.Version, e with { SilentArgs = "/VERYSILENT", Name = "Mới" });
        Assert.Equal(("/VERYSILENT", "Mới"), (u.SilentArgs, u.Name));
        Assert.Equal((e.BlobFile, e.Sha256, e.KeyB64, e.DocsKeyB64, e.BlobSize), (u.BlobFile, u.Sha256, u.KeyB64, u.DocsKeyB64, u.BlobSize));
        Assert.Equal(v + 1, s.Catalog.Version);
        Assert.Equal("/VERYSILENT", Assert.Single(s.Catalog.Packages).SilentArgs);
    }

    [Fact]
    public async Task Replacing_one_extra_repacks_and_keeps_the_others()
    {
        var (root, s, a) = NewVault();
        var e = AddWithExtras(a);
        Assert.Equal(new[] { "a.ini", "b.txt" }, e.Extras);
        var newA = Path.Combine(_tmp, "n", "a.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(newA)!);
        File.WriteAllText(newA, "new-a");
        var kept = new KeptFiles("setup.exe", new() { "b.txt" }, new() { "HuongDan.docx" }, null, null, null);

        var u = await a.UpdatePackageFilesAsync(Draft(e, kept, extras: new() { newA }), false, Ws);

        Assert.NotEqual(e.BlobFile, u.BlobFile);
        Assert.False(File.Exists(Path.Combine(root, "vault", "packages", e.BlobFile)));
        var files = Unpack(root, u);
        Assert.Equal("new-a", files["installer/a.ini"]);
        Assert.Equal("old-b", files["installer/b.txt"]);
        Assert.True(files.ContainsKey("installer/setup.exe"));
        Assert.Equal(new[] { "HuongDan.docx" }, u.Docs);
        Assert.Single(s.Catalog.Packages);
        Assert.Empty(Directory.GetDirectories(Path.Combine(_tmp, "work")).SelectMany(Directory.GetFileSystemEntries));
    }

    [Fact]
    public async Task Repacking_one_version_keeps_the_other_versions()
    {
        var (_, s, a) = NewVault();
        var e = AddWithExtras(a);
        var v2 = a.UpdatePackage(e.Id, SamplePackage.Create(_tmp, version: "2.0"), keepOld: true);
        var kept = new KeptFiles("setup.exe", new() { "a.ini" }, new(), null, null, null);

        var u = await a.UpdatePackageFilesAsync(Draft(e, kept), false, Ws);

        Assert.Equal(new[] { "1.0", "2.0" }, s.Catalog.Packages.Select(p => p.Version).OrderBy(x => x));
        Assert.Contains(s.Catalog.Packages, p => p.BlobFile == v2.BlobFile);
        Assert.Contains(s.Catalog.Packages, p => p.BlobFile == u.BlobFile);
    }

    [Fact]
    public async Task Folder_extras_keep_structure_and_removed_ones_are_dropped()
    {
        var (root, _, a) = NewVault();
        var e = AddWithExtras(a);
        var folder = Path.Combine(_tmp, "Cấu hình");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "x.cfg"), "x");
        File.WriteAllText(Path.Combine(folder, "sub", "y.cfg"), "y");
        var kept = new KeptFiles("setup.exe", new() { "a.ini" }, new(), null, null, null); // b.txt and the doc are removed

        var u = await a.UpdatePackageFilesAsync(Draft(e, kept, entries: PackageSource.ReadFolder(folder)), false, Ws);

        var files = Unpack(root, u);
        Assert.Equal("y", files["installer/Cấu hình/sub/y.cfg"]);
        Assert.Equal("x", files["installer/Cấu hình/x.cfg"]);
        Assert.False(files.ContainsKey("installer/b.txt"));
        Assert.Empty(u.Docs);
        Assert.Contains("Cấu hình/sub/y.cfg", u.Extras!);
    }

    [Fact]
    public void Unsafe_or_duplicate_extra_paths_are_rejected()
    {
        var f = Path.Combine(_tmp, "f.txt");
        File.WriteAllText(f, "x");
        PackageDraft D(params ExtraFile[] xs) => new("i", "n", "1", InstallerType.Exe, f, "", null, 1, new DetectRule(null, null),
            new(), new(), null, null, null, 1, new(), null, null, null, InstallerKind.Unknown, null, xs.ToList());
        Assert.Throws<InvalidDataException>(() => PackageSource.Create(D(new ExtraFile(f, "../evil.txt")), Path.Combine(_tmp, "o1")));
        Assert.Throws<InvalidDataException>(() => PackageSource.Create(D(new ExtraFile(f, @"C:\evil.txt")), Path.Combine(_tmp, "o2")));
        Assert.Throws<InvalidDataException>(() => PackageSource.Create(D(new ExtraFile(f, "d/a.txt"), new ExtraFile(f, "D/A.txt")), Path.Combine(_tmp, "o3")));
    }

    [Fact]
    public async Task Hidden_survives_metadata_edits_repacks_and_new_versions()
    {
        var (_, s, a) = NewVault();
        var e = AddWithExtras(a);
        var hidden = a.UpdatePackageInfo(e.Id, e.Version, e with { Hidden = true });
        Assert.True(Assert.Single(s.Catalog.Packages).Hidden);

        var renamed = a.UpdatePackageInfo(e.Id, e.Version, PackageSource.ApplyMeta(hidden, Draft(hidden, new KeptFiles("setup.exe", null, new(), null, null, null)) with { Name = "Mới" }));
        Assert.True(renamed.Hidden);

        var repacked = await a.UpdatePackageFilesAsync(Draft(renamed, new KeptFiles("setup.exe", new() { "a.ini" }, new(), null, null, null)), false, Ws);
        Assert.True(repacked.Hidden);

        var v2 = a.UpdatePackage(e.Id, SamplePackage.Create(_tmp, version: "2.0"), keepOld: false);
        Assert.True(v2.Hidden);
    }
}
