using Installer.Core.Model;
using Installer.Core.Platform;
using Installer.Core.Tests.TestData;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Platform;

public class IconExtractorTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_test_" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public IconExtractorTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static bool Has(byte[] b, string s) =>
        b.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(s)) >= 0;

    [Fact]
    public void EncodePng_produces_valid_signature_and_chunks()
    {
        var png = IconExtractor.EncodePng(2, 2, new byte[16]);
        Assert.Equal(Sig, png[..8]);
        Assert.True(Has(png, "IHDR")); Assert.True(Has(png, "IDAT")); Assert.True(Has(png, "IEND"));
    }

    [Fact]
    public void Extract_from_notepad_returns_png()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        var png = IconExtractor.ExtractPng(path);
        Assert.NotNull(png);
        Assert.Equal(Sig, png![..8]);
    }

    [Fact]
    public void Extract_from_missing_file_returns_null() =>
        Assert.Null(IconExtractor.ExtractPng(Path.Combine(_tmp, "khong-co.exe")));

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\",3", "C:\\a b\\x.exe", 3)]
    [InlineData("C:\\x.ico", "C:\\x.ico", 0)]
    [InlineData("C:\\x.exe,-12", "C:\\x.exe", -12)]
    public void ParseDisplayIcon_cases(string value, string path, int index) =>
        Assert.Equal((path, index), IconExtractor.ParseDisplayIcon(value));

    VaultAdmin NewAdmin(Func<string, byte[]?>? fromExe = null)
    {
        var root = Path.Combine(_tmp, "Kho Dự án");
        VaultStore.Create(root, "admin", "pw-1");
        return new VaultAdmin(VaultStore.Open(root).Unlock("admin", "pw-1", T0), fromExe);
    }

    [Fact]
    public void Icon_png_in_folder_is_used_and_oversize_rejected()
    {
        var a = NewAdmin();
        var png = IconExtractor.EncodePng(1, 1, new byte[4]);
        var src = SamplePackage.Create(_tmp);
        File.WriteAllBytes(Path.Combine(src, "icon.png"), png);
        Assert.Equal(Convert.ToBase64String(png), a.AddPackage(src).IconPngB64);

        var big = SamplePackage.Create(_tmp, "big");
        File.WriteAllBytes(Path.Combine(big, "icon.png"), Sig.Concat(new byte[25 * 1024]).ToArray());
        Assert.Equal("icon.png phải là ảnh PNG ≤ 24 KB", Assert.Throws<InvalidDataException>(() => a.AddPackage(big)).Message);

        var bad = SamplePackage.Create(_tmp, "bad");
        File.WriteAllBytes(Path.Combine(bad, "icon.png"), new byte[100]);
        Assert.Throws<InvalidDataException>(() => a.AddPackage(bad));
    }

    [Fact]
    public void Icon_falls_back_to_exe_extractor_and_drops_oversize()
    {
        var small = new byte[] { 1, 2, 3 };
        string? seen = null;
        var a = NewAdmin(p => { seen = p; return small; });
        var e = a.AddPackage(SamplePackage.Create(_tmp));
        Assert.Equal(Convert.ToBase64String(small), e.IconPngB64);
        Assert.EndsWith(Path.Combine("installer", "setup.exe"), seen);

        var a2 = NewAdminAt("k2", _ => new byte[30 * 1024]);
        Assert.Null(a2.AddPackage(SamplePackage.Create(_tmp, "o2")).IconPngB64);
    }

    VaultAdmin NewAdminAt(string name, Func<string, byte[]?> f)
    {
        var root = Path.Combine(_tmp, name);
        VaultStore.Create(root, "admin", "pw-1");
        return new VaultAdmin(VaultStore.Open(root).Unlock("admin", "pw-1", T0), f);
    }
}
