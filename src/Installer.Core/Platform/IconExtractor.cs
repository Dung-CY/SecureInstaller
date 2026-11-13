using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Installer.Core.Platform;

public static class IconExtractor
{
    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint PrivateExtractIconsW(string file, int index, int cx, int cy, IntPtr[] icons, uint[]? ids, uint count, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("user32.dll", ExactSpelling = true)] static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("gdi32.dll", ExactSpelling = true)] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll", ExactSpelling = true)] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", ExactSpelling = true)] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", ExactSpelling = true)] static extern int GetObjectW(IntPtr h, int cb, out BITMAP bmp);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bmi, uint usage);

    public static byte[]? ExtractPng(string filePath, int index = 0, int size = 48)
    {
        var icon = IntPtr.Zero;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            var icons = new IntPtr[1];
            var n = PrivateExtractIconsW(filePath, index, size, size, icons, null, 1, 0);
            if (n == 0 || n == uint.MaxValue || icons[0] == IntPtr.Zero) return null;
            icon = icons[0];
            return IconToPng(icon);
        }
        catch { return null; }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); }
    }

    static byte[]? IconToPng(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info)) return null;
        var dc = IntPtr.Zero;
        try
        {
            if (info.hbmColor == IntPtr.Zero) return null;
            if (GetObjectW(info.hbmColor, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
            int w = bm.bmWidth, h = bm.bmHeight;
            if (w <= 0 || h <= 0 || w > 512 || h > 512) return null;
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;

            var bgra = ReadBits(dc, info.hbmColor, w, h);
            if (bgra is null) return null;

            var mask = info.hbmMask != IntPtr.Zero ? ReadBits(dc, info.hbmMask, w, h) : null;
            ApplyMask(bgra, mask);
            return EncodePng(w, h, BgraToRgba(bgra));
        }
        finally
        {
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    /// <summary>Legacy icons without alpha: if every alpha byte is 0, derive it from the AND mask (black = opaque, white = transparent; null = opaque).</summary>
    internal static void ApplyMask(byte[] bgra, byte[]? maskBgra)
    {
        for (var i = 3; i < bgra.Length; i += 4) if (bgra[i] != 0) return;
        for (var i = 0; i < bgra.Length; i += 4)
            bgra[i + 3] = maskBgra is null || (maskBgra[i] == 0 && maskBgra[i + 1] == 0 && maskBgra[i + 2] == 0) ? (byte)255 : (byte)0;
    }

    internal static byte[] BgraToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = bgra[i + 3];
        }
        return rgba;
    }

    static byte[]? ReadBits(IntPtr dc, IntPtr hbm, int w, int h)
    {
        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32
        };
        var buf = new byte[w * h * 4];
        return GetDIBits(dc, hbm, 0, (uint)h, buf, ref bmi, 0) == h ? buf : null;
    }

    public static byte[] EncodePng(int w, int h, byte[] rgba)
    {
        if (w <= 0 || h <= 0 || rgba.Length != w * h * 4) throw new ArgumentException("Kích thước ảnh không hợp lệ");
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, w); WriteBe(ihdr, 4, h);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(ms, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = w * 4;
            for (var y = 0; y < h; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * stride, stride);
            }
        }
        Chunk(ms, "IDAT", raw.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    static void WriteBe(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBe(len, 0, data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, body, 4, data.Length);
        s.Write(body);
        var crc = new byte[4]; WriteBe(crc, 0, unchecked((int)Crc32(body)));
        s.Write(crc);
    }

    static readonly uint[] CrcTable = BuildTable();
    static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
    static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>Parses a registry DisplayIcon value: optional quotes, optional trailing ",index".</summary>
    public static (string Path, int Index) ParseDisplayIcon(string value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return ("", 0);
        string path; var rest = "";
        if (v[0] == '"')
        {
            var close = v.IndexOf('"', 1);
            if (close < 0) path = v[1..];
            else { path = v[1..close]; rest = v[(close + 1)..]; }
        }
        else
        {
            var comma = v.LastIndexOf(',');
            if (comma > 0 && int.TryParse(v[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { path = v[..comma]; rest = v[comma..]; }
            else path = v;
        }
        var idx = 0;
        var r = rest.Trim();
        if (r.StartsWith(',') && int.TryParse(r[1..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) idx = parsed;
        return (Environment.ExpandEnvironmentVariables(path.Trim()), idx);
    }
}
