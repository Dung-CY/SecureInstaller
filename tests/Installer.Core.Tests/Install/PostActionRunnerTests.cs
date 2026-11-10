using System.Security.Cryptography;
using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.Core.Tests.Install;

public class PostActionRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SItest_" + Guid.NewGuid().ToString("N"));
    public PostActionRunnerTests() => Directory.CreateDirectory(Path.Combine(_root, "extras"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private string Extras => Path.Combine(_root, "extras");

    [Fact]
    public void Copy_creates_dirs_and_matches_hash()
    {
        File.WriteAllText(Path.Combine(Extras, "a.ini"), "hello Việt");
        var dst = Path.Combine(_root, "Dự án 1", "out", "a.ini");
        var r = PostActionRunner.Run(new[] { new PostAction("copy", "a.ini", dst) }, Extras);
        Assert.True(r[0].Ok, r[0].Error);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(Extras, "a.ini"))),
                     SHA256.HashData(File.ReadAllBytes(dst)));
        // overwrite
        File.WriteAllText(Path.Combine(Extras, "a.ini"), "v2");
        r = PostActionRunner.Run(new[] { new PostAction("copy", "a.ini", dst) }, Extras);
        Assert.True(r[0].Ok);
        Assert.Equal("v2", File.ReadAllText(dst));
    }

    [Fact]
    public void Copy_rejects_parent_traversal()
    {
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "s");
        var dst = Path.Combine(_root, "o", "s.txt");
        var r = PostActionRunner.Run(new[]
        {
            new PostAction("copy", @"..\secret.txt", dst),
            new PostAction("copy", Path.Combine(_root, "secret.txt"), dst),
            new PostAction("copy", null, dst),
        }, Extras);
        Assert.All(r, x => { Assert.False(x.Ok); Assert.Equal("Đường dẫn nguồn không hợp lệ", x.Error); });
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void Delete_missing_is_ok()
    {
        var r = PostActionRunner.Run(new[] { new PostAction("delete", null, Path.Combine(_root, "nope")) }, Extras);
        Assert.True(r[0].Ok);
    }

    [Fact]
    public void Delete_removes_file_and_dir()
    {
        var d = Path.Combine(_root, "d"); Directory.CreateDirectory(Path.Combine(d, "x"));
        var f = Path.Combine(_root, "f.txt"); File.WriteAllText(f, "1");
        var r = PostActionRunner.Run(new[] { new PostAction("delete", null, d), new PostAction("DELETE", null, f) }, Extras);
        Assert.All(r, x => Assert.True(x.Ok, x.Error));
        Assert.False(Directory.Exists(d)); Assert.False(File.Exists(f));
    }

    [Fact]
    public void Delete_refuses_protected_roots()
    {
        // Refusal must happen before any IO, so these are safe.
        var targets = new[]
        {
            @"C:\", @"C:", "%SystemRoot%", @"%SystemRoot%\System32", "%ProgramFiles%",
            "%ProgramFiles(x86)%", "%ProgramData%", "%UserProfile%", @"%SystemRoot%\"
        };
        var r = PostActionRunner.Run(targets.Select(t => new PostAction("delete", null, t)), Extras);
        Assert.All(r, x => Assert.False(x.Ok));
        Assert.Equal("Không được xóa thư mục hệ thống", r[0].Error);
        Assert.Equal("Không được xóa thư mục hệ thống", r[2].Error);
        Assert.Equal("Không được xóa thư mục hệ thống", r[7].Error);
    }

    [Fact]
    public void Failure_does_not_stop_following_actions()
    {
        File.WriteAllText(Path.Combine(Extras, "a.ini"), "x");
        var dst = Path.Combine(_root, "ok", "a.ini");
        var r = PostActionRunner.Run(new[]
        {
            new PostAction("copy", "missing.ini", Path.Combine(_root, "z", "m.ini")),
            new PostAction("bogus", null, dst),
            new PostAction("copy", "a.ini", @"relative\x"),
            new PostAction("copy", "a.ini", dst),
        }, Extras);
        Assert.Equal(4, r.Count);
        Assert.False(r[0].Ok);
        Assert.Equal("Loại thao tác không hỗ trợ", r[1].Error);
        Assert.Equal("Đường dẫn đích không hợp lệ", r[2].Error);
        Assert.True(r[3].Ok);
        Assert.True(File.Exists(dst));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathNameW(string longPath, System.Text.StringBuilder shortPath, uint cch);

    [Fact]
    public void Delete_refuses_extended_and_unc_prefixes()
    {
        var targets = new[]
        {
            @"\\?\" + Environment.ExpandEnvironmentVariables("%SystemRoot%"),
            @"\\.\C:\Windows",
            @"\\localhost\C$\Windows",
        };
        var r = PostActionRunner.Run(targets.Select(t => new PostAction("delete", null, t)), Extras);
        Assert.All(r, x => { Assert.False(x.Ok); Assert.Equal("Đường dẫn đích không hợp lệ", x.Error); });
    }

    [Fact]
    public void Delete_refuses_short_name_of_program_files()
    {
        var pf = Environment.ExpandEnvironmentVariables("%ProgramFiles%");
        var sb = new System.Text.StringBuilder(512);
        GetShortPathNameW(pf, sb, 512);
        var shortName = sb.ToString();
        if (shortName.Length == 0 || shortName.Equals(pf, StringComparison.OrdinalIgnoreCase)) return; // 8.3 disabled
        Assert.True(PostActionRunner.IsProtectedPath(shortName));
        var r = PostActionRunner.Run(new[] { new PostAction("delete", null, shortName) }, Extras);
        Assert.False(r[0].Ok);
        Assert.Equal("Không được xóa thư mục hệ thống", r[0].Error);
    }

    [Fact]
    public void Delete_allows_subfolder_of_protected_root_path_check()
    {
        var p = Environment.ExpandEnvironmentVariables("%ProgramData%") + Path.DirectorySeparatorChar + "SI_test_nonexistent";
        Assert.False(PostActionRunner.IsProtectedPath(p));
    }

    [Fact]
    public void Copy_rejects_prefix_confusion()
    {
        Directory.CreateDirectory(Path.Combine(_root, "extras2"));
        File.WriteAllText(Path.Combine(_root, "extras2", "x.txt"), "x");
        var dst = Path.Combine(_root, "o2", "x.txt");
        var r = PostActionRunner.Run(new[] { new PostAction("copy", @"..\extras2\x.txt", dst) }, Extras);
        Assert.False(r[0].Ok);
        Assert.Equal("Đường dẫn nguồn không hợp lệ", r[0].Error);
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void Delete_refuses_ads_suffix()
    {
        var sr = Environment.ExpandEnvironmentVariables("%SystemRoot%");
        var r = PostActionRunner.Run(new[]
        {
            new PostAction("delete", null, sr + "::$INDEX_ALLOCATION"),
            new PostAction("delete", null, Path.Combine(_root, "f.txt:stream")),
        }, Extras);
        Assert.All(r, x => { Assert.False(x.Ok); Assert.Equal("Đường dẫn đích không hợp lệ", x.Error); });
    }
}
