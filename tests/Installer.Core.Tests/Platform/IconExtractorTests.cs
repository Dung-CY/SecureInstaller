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

    static uint Crc(byte[] d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }

    static bool Has(byte[] b, string s) =>
        b.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(s)) >= 0;

    [Fact]
    public void EncodePng_round_trips_with_valid_crcs_and_header()
    {
        const int w = 3, h = 2;
        var rgba = Enumerable.Range(0, w * h * 4).Select(i => (byte)(i * 7 + 1)).ToArray();
        var png = IconExtractor.EncodePng(w, h, rgba);
        Assert.Equal(Sig, png[..8]);

        var pos = 8; var names = new List<string>(); byte[]? ihdr = null; var idat = new MemoryStream();
        while (pos < png.Length)
        {
            var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var typeAndData = png.AsSpan(pos + 4, 4 + len).ToArray();
            var crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            Assert.Equal(Crc(typeAndData), crc);
            var name = System.Text.Encoding.ASCII.GetString(typeAndData, 0, 4);
            names.Add(name);
            if (name == "IHDR") ihdr = typeAndData[4..];
            if (name == "IDAT") idat.Write(typeAndData, 4, len);
            pos += 12 + len;
        }
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, names);
        Assert.Equal(w, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(ihdr!.AsSpan(0)));
        Assert.Equal(h, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4)));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, ihdr[8..13]);

        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
        var data = raw.ToArray();
        Assert.Equal(h * (1 + 4 * w), data.Length);
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(0, data[y * (1 + 4 * w)]);
            Assert.Equal(rgba.AsSpan(y * 4 * w, 4 * w).ToArray(), data.AsSpan(y * (1 + 4 * w) + 1, 4 * w).ToArray());
        }
    }

    [Fact]
    public void ApplyMask_leaves_existing_alpha_untouched()
    {
        var bgra = new byte[] { 1, 2, 3, 0, 4, 5, 6, 128 };
        IconExtractor.ApplyMask(bgra, new byte[8]);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 128 }, bgra);
    }

    [Fact]
    public void ApplyMask_black_mask_is_opaque_white_is_transparent()
    {
        var bgra = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 };
        IconExtractor.ApplyMask(bgra, new byte[] { 0, 0, 0, 0, 255, 255, 255, 0 });
        Assert.Equal(255, bgra[3]);
        Assert.Equal(0, bgra[7]);
    }

    [Fact]
    public void ApplyMask_null_mask_is_fully_opaque()
    {
        var bgra = new byte[8];
        IconExtractor.ApplyMask(bgra, null);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255 }, bgra);
    }

    [Fact]
    public void BgraToRgba_swaps_channels()
    {
        Assert.Equal(new byte[] { 3, 2, 1, 4 }, IconExtractor.BgraToRgba(new byte[] { 1, 2, 3, 4 }));
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
    [InlineData("\"C:\\x.exe\"", "C:\\x.exe", 0)]
    [InlineData("C:\\x.exe , 3", "C:\\x.exe", 3)]
    [InlineData("\"C:\\x.exe\" , -5", "C:\\x.exe", -5)]
    [InlineData("%SystemRoot%\\x.exe,1", "EXPAND", 1)]
    public void ParseDisplayIcon_cases(string value, string path, int index) =>
        Assert.Equal((path == "EXPAND" ? Environment.ExpandEnvironmentVariables("%SystemRoot%\\x.exe") : path, index), IconExtractor.ParseDisplayIcon(value));

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
