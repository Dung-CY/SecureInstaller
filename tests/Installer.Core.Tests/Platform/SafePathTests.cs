using Installer.Core.Errors;
using Installer.Core.Platform;

namespace Installer.Core.Tests.Platform;

public class SafePathTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_sp_" + Guid.NewGuid().ToString("N"), "Dự án 1");
    public SafePathTests() => Directory.CreateDirectory(_tmp);
    public void Dispose()
    {
        try { var l = Path.Combine(_tmp, "link"); if (Directory.Exists(l)) Directory.Delete(l, false); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(_tmp)!, true); } catch { }
    }

    internal static void MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false };
        using (var p = System.Diagnostics.Process.Start(psi)!) p.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J failed");
    }

    [Fact]
    public void Junction_parent_is_rejected()
    {
        var real = Path.Combine(_tmp, "real"); Directory.CreateDirectory(real);
        var link = Path.Combine(_tmp, "link");
        MakeJunction(link, real);
        Assert.Throws<UnsafePathException>(() => SafePath.EnsureNoReparseAncestors(Path.Combine(link, "a", "b")));
    }

    [Fact]
    public void Missing_tail_components_are_allowed()
        => SafePath.EnsureNoReparseAncestors(Path.Combine(_tmp, "x", "y", "z"));

    [Fact]
    public void Plain_path_passes() => SafePath.EnsureNoReparseAncestors(_tmp);

    [Theory]
    [InlineData(@"\server\share\a")]
    [InlineData(@"\?\C:\a")]
    public void Unc_and_extended_throw(string p)
        => Assert.Throws<UnsafePathException>(() => SafePath.EnsureNoReparseAncestors(p));
}
