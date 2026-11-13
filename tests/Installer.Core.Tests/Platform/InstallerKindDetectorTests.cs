using System.Text;
using Installer.Core.Model;
using Installer.Core.Platform;

namespace Installer.Core.Tests.Platform;

public class InstallerKindDetectorTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "SI_kind_" + Guid.NewGuid().ToString("N"), "Dự án 1");

    public InstallerKindDetectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch { } }

    string File_(string name, byte[] bytes) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, bytes); return p; }

    static byte[] Padded(string marker, int at, int total = 3 * 1024 * 1024)
    {
        var b = new byte[total];
        Encoding.ASCII.GetBytes(marker).CopyTo(b, at);
        return b;
    }

    /// <summary>MZ + PE header + one section table whose first section is called <paramref name="section"/>.</summary>
    static byte[] MinimalPe(string section)
    {
        var b = new byte[1024];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
        BitConverter.GetBytes(0x00004550).CopyTo(b, 0x80);
        BitConverter.GetBytes((ushort)1).CopyTo(b, 0x80 + 6);
        BitConverter.GetBytes((ushort)0xE0).CopyTo(b, 0x80 + 20);
        Encoding.ASCII.GetBytes(section).CopyTo(b, 0x80 + 24 + 0xE0);
        return b;
    }

    [Theory]
    [InlineData("a.msi", InstallerKind.Msi)] [InlineData("a.ZIP", InstallerKind.Zip)]
    [InlineData("a.ps1", InstallerKind.Script)] [InlineData("a.cmd", InstallerKind.Script)] [InlineData("a.bat", InstallerKind.Script)]
    [InlineData("a.txt", InstallerKind.Unknown)]
    public void Extension_decides_non_exe(string name, InstallerKind kind) =>
        Assert.Equal(kind, InstallerKindDetector.Detect(File_(name, new byte[10])));

    [Theory]
    [InlineData("Inno Setup Setup Data (6.2.0)", InstallerKind.Inno)]
    [InlineData("JR.Inno.Setup", InstallerKind.Inno)]
    [InlineData("Nullsoft.NSIS.exehead", InstallerKind.Nsis)]
    [InlineData("NullsoftInst", InstallerKind.Nsis)]
    [InlineData("InstallShield", InstallerKind.InstallShield)]
    public void Exe_markers(string marker, InstallerKind kind) =>
        Assert.Equal(kind, InstallerKindDetector.Detect(File_("s.exe", Padded(marker, 5000))));

    [Fact]
    public void Marker_straddling_a_chunk_boundary_is_found()
    {
        var m = "Nullsoft.NSIS.exehead";
        Assert.Equal(InstallerKind.Nsis, InstallerKindDetector.Detect(File_("s.exe", Padded(m, 1024 * 1024 - 7))));
    }

    [Fact]
    public void Marker_beyond_the_32MB_limit_is_ignored() =>
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(
            File_("s.exe", Padded("Nullsoft.NSIS.exehead", 33 * 1024 * 1024, 34 * 1024 * 1024))));

    [Fact]
    public void Wixburn_section_means_Burn_even_with_other_markers()
    {
        var pe = MinimalPe(".wixburn");
        Encoding.ASCII.GetBytes("NullsoftInst").CopyTo(pe, 600);
        Assert.Equal(InstallerKind.Burn, InstallerKindDetector.Detect(File_("b.exe", pe)));
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(File_("c.exe", MinimalPe(".text"))));
    }

    [Fact]
    public void Plain_text_missing_file_and_notepad_are_Unknown()
    {
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(File_("t.exe", Encoding.ASCII.GetBytes("hello"))));
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(Path.Combine(_dir, "none.exe")));
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(
            Path.Combine(Environment.SystemDirectory, "..", "notepad.exe")));
    }

    [Fact]
    public void Silent_args_follow_the_table()
    {
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", InstallerKindDetector.SilentArgsFor(InstallerKind.Inno));
        Assert.Equal("/S", InstallerKindDetector.SilentArgsFor(InstallerKind.Nsis));
        Assert.Equal("/quiet /norestart", InstallerKindDetector.SilentArgsFor(InstallerKind.Burn));
        Assert.Equal("/s /v\"/qn\"", InstallerKindDetector.SilentArgsFor(InstallerKind.InstallShield));
        Assert.Equal("", InstallerKindDetector.SilentArgsFor(InstallerKind.Msi));
        Assert.Equal("", InstallerKindDetector.SilentArgsFor(InstallerKind.Unknown));
    }

    // Optional: runs only when fetch-demo-installers.ps1 has filled samples\.cache.
    [Fact]
    public void Real_samples_are_detected_when_cached()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "samples", "demo-installers.json"))) root = Path.GetDirectoryName(root);
        if (root is null) return;
        var items = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "samples", "demo-installers.json"))).RootElement;
        foreach (var it in items.EnumerateArray())
        {
            var f = Path.Combine(root, "samples", ".cache", "installers", it.GetProperty("file").GetString()!);
            if (!File.Exists(f)) continue;
            Assert.Equal(Enum.Parse<InstallerKind>(it.GetProperty("kind").GetString()!, true), InstallerKindDetector.Detect(f));
        }
    }
}
