using Installer.Core.Tests.TestData;
using Installer.Packager;

namespace Installer.Core.Tests.Packager;

public class ArgsTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_args_" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly string Usb = Path.Combine("D:", "Dự án 1", "usb");

    public ArgsTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void Parse_add_package_positionals_and_options()
    {
        var p = Args.Parse(new[] { "add-package", Usb, "pkg", "--user", "admin" });
        Assert.Equal("add-package", p.Command);
        Assert.Equal(new[] { Usb, "pkg" }, p.Positionals);
        Assert.Equal("admin", p.Options["user"]);
    }

    [Fact]
    public void Parse_flag_without_value()
    {
        var p = Args.Parse(new[] { "update-package", "r", "id", "f", "--keep-old", "--user", "a" });
        Assert.True(p.Options.ContainsKey("keep-old"));
        Assert.Null(p.Options["keep-old"]);
        Assert.Equal("a", p.Options["user"]);
    }

    [Fact]
    public void Parse_unknown_command_throws()
    {
        Assert.Throws<ArgumentException>(() => Args.Parse(new[] { "frobnicate", "x" }));
        Assert.Throws<ArgumentException>(() => Args.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Run_init_then_add_package_returns_0()
    {
        var root = Path.Combine(_tmp, "kho dữ liệu");
        Assert.Equal(0, Program.Run(new[] { "init", root, "--user", "admin", "--password", "pw-123456" }));
        var src = SamplePackage.Create(_tmp);
        Assert.Equal(0, Program.Run(new[] { "add-package", root, src, "--user", "admin", "--password", "pw-123456" }));
        Assert.Equal(1, Program.Run(new[] { "add-package", root, src, "--user", "admin", "--password", "wrong" }));
        Assert.Equal(2, Program.Run(new[] { "add-package", root }));
    }
}
