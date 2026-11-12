using Installer.Core.Platform;

namespace Installer.Core.Tests.Platform;

public class InstallerInfoReaderTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_info_" + Guid.NewGuid().ToString("N"), "Dự án 1");

    public InstallerInfoReaderTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_tmp)!, true); } catch { } }

    [Fact]
    public void Exe_returns_the_file_product_version()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        var (_, version, code) = InstallerInfoReader.Read(notepad);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Null(code);
    }

    [Fact]
    public void Text_file_returns_nulls()
    {
        var p = Path.Combine(_tmp, "ghi chú.txt");
        File.WriteAllText(p, "hello");
        Assert.Equal((null, null, null), InstallerInfoReader.Read(p));
    }

    [Fact]
    public void Broken_msi_or_missing_file_returns_nulls()
    {
        var p = Path.Combine(_tmp, "hỏng.msi");
        File.WriteAllText(p, "not an msi");
        Assert.Equal((null, null, null), InstallerInfoReader.Read(p));
        Assert.Equal((null, null, null), InstallerInfoReader.Read(Path.Combine(_tmp, "không có.exe")));
    }
}
