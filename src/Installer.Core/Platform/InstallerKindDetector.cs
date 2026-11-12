using System.Diagnostics;
using System.Text;
using Installer.Core.Model;

namespace Installer.Core.Platform;

/// <summary>Guesses the installer technology from file bytes only; the file is never executed.</summary>
public static class InstallerKindDetector
{
    const long ScanLimit = 32L * 1024 * 1024;
    const int ChunkSize = 1024 * 1024;

    static readonly byte[] Inno1 = Encoding.ASCII.GetBytes("Inno Setup Setup Data");
    static readonly byte[] Inno2 = Encoding.ASCII.GetBytes("JR.Inno.Setup");
    static readonly byte[] Nsis1 = Encoding.ASCII.GetBytes("Nullsoft.NSIS.exehead");
    static readonly byte[] Nsis2 = Encoding.ASCII.GetBytes("NullsoftInst");
    static readonly byte[] Shield = Encoding.ASCII.GetBytes("InstallShield");
    static readonly int Overlap = new[] { Inno1, Inno2, Nsis1, Nsis2, Shield }.Max(m => m.Length) - 1;

    public static InstallerKind Detect(string path)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".msi": return InstallerKind.Msi;
                case ".zip": return InstallerKind.Zip;
                case ".ps1": case ".cmd": case ".bat": return InstallerKind.Script;
                case ".exe": return DetectExe(path);
                default: return InstallerKind.Unknown;
            }
        }
        catch { return InstallerKind.Unknown; }
    }

    static InstallerKind DetectExe(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (HasSection(fs, ".wixburn")) return InstallerKind.Burn;

        bool inno = false, nsis = false, shield = false;
        var buf = new byte[ChunkSize + Overlap];
        var keep = 0;
        long total = 0;
        fs.Position = 0;
        while (total < ScanLimit && !inno)
        {
            var n = fs.Read(buf, keep, ChunkSize);
            if (n <= 0) break;
            total += n;
            var span = buf.AsSpan(0, keep + n);
            inno = span.IndexOf(Inno1) >= 0 || span.IndexOf(Inno2) >= 0;
            nsis |= span.IndexOf(Nsis1) >= 0 || span.IndexOf(Nsis2) >= 0;
            shield |= span.IndexOf(Shield) >= 0;
            // Carry the tail so a marker straddling two chunks is still seen.
            keep = Math.Min(Overlap, span.Length);
            span[^keep..].CopyTo(buf);
        }
        if (inno) return InstallerKind.Inno;
        if (nsis) return InstallerKind.Nsis;
        if (shield || VersionStringsMentionShield(path)) return InstallerKind.InstallShield;
        return InstallerKind.Unknown;
    }

    static bool VersionStringsMentionShield(string path)
    {
        var v = FileVersionInfo.GetVersionInfo(path);
        return new[] { v.CompanyName, v.ProductName, v.FileDescription, v.LegalCopyright, v.InternalName }
            .Any(s => s?.Contains("InstallShield", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Walks the PE section table looking for a section called <paramref name="name"/>.</summary>
    static bool HasSection(FileStream fs, string name)
    {
        var head = new byte[64];
        fs.Position = 0;
        if (fs.Read(head, 0, 64) < 64 || head[0] != 'M' || head[1] != 'Z') return false;
        var pe = BitConverter.ToInt32(head, 0x3C);
        if (pe < 0 || pe > fs.Length - 24) return false;
        var hdr = new byte[24];
        fs.Position = pe;
        if (fs.Read(hdr, 0, 24) < 24 || BitConverter.ToUInt32(hdr, 0) != 0x00004550) return false;
        int sections = BitConverter.ToUInt16(hdr, 6), optSize = BitConverter.ToUInt16(hdr, 20);
        if (sections > 96) return false;
        fs.Position = pe + 24 + optSize;
        var sec = new byte[40];
        var want = Encoding.ASCII.GetBytes(name);
        for (var i = 0; i < sections; i++)
        {
            if (fs.Read(sec, 0, 40) < 40) return false;
            if (sec.AsSpan(0, 8).TrimEnd((byte)0).SequenceEqual(want)) return true;
        }
        return false;
    }

    public static string SilentArgsFor(InstallerKind kind) => kind switch
    {
        InstallerKind.Inno => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
        InstallerKind.Nsis => "/S",
        InstallerKind.Burn => "/quiet /norestart",
        InstallerKind.InstallShield => "/s /v\"/qn\"",
        _ => ""
    };
}
