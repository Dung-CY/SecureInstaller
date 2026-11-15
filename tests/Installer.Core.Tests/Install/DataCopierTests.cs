using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class DataCopierTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "SI_data Dự án " + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Src(params (string Rel, string Text)[] files)
    {
        var src = Path.Combine(_root, "DuLieu", "qlvbdh");
        foreach (var (rel, text) in files)
        {
            var p = Path.Combine(src, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }
        return src;
    }

    [Fact]
    public void Copies_keeping_names_and_applies_renames_with_unit()
    {
        var src = Src(("config.xml", "c"), ("mau/bieu mau.docx", "m"), ("giu nguyen.txt", "g"));
        var target = Path.Combine(_root, "Cài đặt", "QLVBDH");
        var d = new DataCopy(target, new() { "config.xml => config_{DonVi}.xml", @"mau\bieu mau.docx => Mẫu {DonVi}.docx" });

        Assert.Equal(3, DataCopier.Copy(src, d, "LamThao"));

        Assert.Equal("c", File.ReadAllText(Path.Combine(target, "config_LamThao.xml")));
        Assert.Equal("m", File.ReadAllText(Path.Combine(target, "Mẫu LamThao.docx")));
        Assert.Equal("g", File.ReadAllText(Path.Combine(target, "giu nguyen.txt")));
        Assert.False(File.Exists(Path.Combine(target, "config.xml")));
    }

    [Fact]
    public void Missing_source_folder_copies_nothing() =>
        Assert.Equal(0, DataCopier.Copy(Path.Combine(_root, "khong-co"), new DataCopy(Path.Combine(_root, "t"), null), "X"));

    [Theory]
    [InlineData("a.txt => ../thoat.txt")]
    [InlineData("a.txt => C:\\Windows\\x.txt")]
    [InlineData("a.txt")]
    [InlineData("=> b.txt")]
    public void Bad_rename_lines_are_rejected(string line) =>
        Assert.NotNull(DataCopier.Validate(new DataCopy(@"C:\App", new() { line })));

    [Fact]
    public void Rename_escaping_the_target_through_the_unit_is_refused()
    {
        var src = Src(("a.txt", "x"));
        var d = new DataCopy(Path.Combine(_root, "t"), new() { "a.txt => {DonVi}/a.txt" });
        Assert.Throws<InvalidDataException>(() => DataCopier.Copy(src, d, ".."));
        Assert.False(File.Exists(Path.Combine(_root, "a.txt")));
    }
}
