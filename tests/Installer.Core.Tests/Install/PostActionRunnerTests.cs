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
}
