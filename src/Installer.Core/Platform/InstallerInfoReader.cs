using System.Diagnostics;
using Installer.Core.Install;

namespace Installer.Core.Platform;

/// <summary>Reads name/version metadata from an installer file without ever running it.</summary>
public static class InstallerInfoReader
{
    public static (string? Name, string? Version, string? ProductCode) Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null, null);
            var ext = Path.GetExtension(path);
            if (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase)) return ReadMsi(path);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                return (Blank(v.ProductName), Blank(v.ProductVersion), null);
            }
        }
        catch { }
        return (null, null, null);
    }

    static (string?, string?, string?) ReadMsi(string path)
    {
        // Needs the Windows Installer service; any non-zero return (bad file, service off) just means "no info".
        if (MsiNative.MsiOpenPackageExW(path, MsiNative.MSIOPENPACKAGEFLAGS_IGNOREMACHINESTATE, out var h) != 0)
            return (null, null, null);
        try { return (Prop(h, "ProductName"), Prop(h, "ProductVersion"), Prop(h, "ProductCode")); }
        finally { MsiNative.MsiCloseHandle(h); }
    }

    static string? Prop(IntPtr h, string name)
    {
        const uint ErrorMoreData = 234;
        var buf = new char[256];
        var len = (uint)buf.Length;
        var rc = MsiNative.MsiGetProductPropertyW(h, name, buf, ref len);
        if (rc == ErrorMoreData)
        {
            buf = new char[++len];
            rc = MsiNative.MsiGetProductPropertyW(h, name, buf, ref len);
        }
        return rc == 0 ? Blank(new string(buf, 0, (int)len)) : null;
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
