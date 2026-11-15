using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class DataCopierTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "SI_data Dự án " + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Src(params string[] files)
    {
        var src = Path.Combine(_root, "DuLieu", "qlvbdh");
        foreach (var rel in files)
        {
            var p = Path.Combine(src, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, rel);
        }
        return src;
    }

    string Target => Path.Combine(_root, "Cài đặt", "QLVBDH");
    string[] Copied() => Directory.GetFiles(Target, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Target, f)).OrderBy(x => x).ToArray();

    [Fact]
    public void Picks_the_units_file_under_a_common_name_and_copies_only_listed_files()
    {
        var src = Src("LamThao.txt", "HoaBinh.txt", "ThanhBa.txt", "huongdan.pdf", "khong-liet-ke.ini");
        var d = new DataCopy(Target, new() { "{DonVi}.txt => config.txt", "huongdan.pdf" });

        var r = DataCopier.Copy(src, d, "LamThao");

        Assert.Equal((2, 0), (r.Copied, r.Missing.Count));
        Assert.Equal(new[] { "config.txt", "huongdan.pdf" }, Copied());
        Assert.Equal("LamThao.txt", File.ReadAllText(Path.Combine(Target, "config.txt")));
    }

    [Fact]
    public void A_unit_without_its_file_is_reported_not_copied()
    {
        var src = Src("HoaBinh.txt");
        var r = DataCopier.Copy(src, new DataCopy(Target, new() { "{DonVi}.txt => config.txt" }), "LamThao");
        Assert.Equal(0, r.Copied);
        Assert.Equal(new[] { "LamThao.txt" }, r.Missing);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public void Missing_data_folder_reports_every_file() =>
        Assert.Equal(new[] { "LamThao.docconf" },
            DataCopier.Copy(Path.Combine(_root, "khong-co"), new DataCopy(Target, new() { "{DonVi}.docconf => app.docconf" }), "LamThao").Missing);

    [Theory]
    [InlineData("a.txt => ../thoat.txt")]
    [InlineData("a.txt => C:\\Windows\\x.txt")]
    [InlineData("../a.txt")]
    [InlineData("=> b.txt")]
    public void Bad_lines_are_rejected(string line) =>
        Assert.NotNull(DataCopier.Validate(new DataCopy(@"C:\App", new() { line })));

    [Fact]
    public void An_empty_list_is_rejected() =>
        Assert.Equal("Nhập ít nhất một file cần chép", DataCopier.Validate(new DataCopy(@"C:\App", new() { " " })));

    [Fact]
    public void A_unit_that_would_climb_out_is_refused()
    {
        var src = Src("a.txt");
        Assert.Throws<InvalidDataException>(() => DataCopier.Copy(src, new DataCopy(Target, new() { "a.txt => {DonVi}/a.txt" }), ".."));
        Assert.False(File.Exists(Path.Combine(_root, "Cài đặt", "a.txt")));
    }
}
