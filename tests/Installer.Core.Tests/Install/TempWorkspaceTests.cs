using System.Security.AccessControl;
using System.Security.Principal;
using Installer.Core.Errors;
using Installer.Core.Install;
using Installer.Core.Platform;

namespace Installer.Core.Tests.Install;

public class TempWorkspaceTests
{
    private static string NewBase() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SItest_" + Guid.NewGuid().ToString("N"), "work");

    private static void Cleanup(string b)
    {
        try { Directory.Delete(System.IO.Path.GetDirectoryName(b)!, true); } catch { }
    }

    [Fact]
    public void Workspace_is_created_under_given_base()
    {
        var b = NewBase();
        try
        {
            using var ws = TempWorkspace.Create(b);
            Assert.StartsWith(b + "\\", ws.Path);
            Assert.True(Directory.Exists(ws.Path));
        }
        finally { Cleanup(b); }
    }

    [Fact]
    public void Elevated_branch_acl_has_admins_and_system_only_and_owner_admins()
    {
        var sec = SecureDirectory.BuildSecurity(true);
        Assert.True(sec.AreAccessRulesProtected);
        var rules = sec.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        var ba = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        var sy = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        Assert.True(new HashSet<string> { ba, sy }.SetEquals(rules.Select(r => r.IdentityReference.Value)));
        Assert.Equal(ba, sec.GetOwner(typeof(SecurityIdentifier))!.Value);
    }

    [Fact]
    public void Non_elevated_branch_includes_current_user()
    {
        var b = NewBase();
        try
        {
            using var ws = TempWorkspace.Create(b, () => false);
            var rules = new DirectoryInfo(ws.Path).GetAccessControl()
                .GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Assert.Contains(WindowsIdentity.GetCurrent().User!.Value, rules.Select(r => r.IdentityReference.Value));
        }
        finally { Cleanup(b); }
    }

    [Fact]
    public void Base_that_is_a_junction_throws_WorkspaceUnsafe()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SItest_" + Guid.NewGuid().ToString("N"));
        var target = System.IO.Path.Combine(root, "target");
        var junction = System.IO.Path.Combine(root, "work");
        Directory.CreateDirectory(target);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false };
            using (var p = System.Diagnostics.Process.Start(psi)!) p.WaitForExit();
            Assert.True(Directory.Exists(junction), "mklink /J failed");
            Assert.Throws<WorkspaceUnsafeException>(() => TempWorkspace.Create(junction));
            Assert.Throws<WorkspaceUnsafeException>(() => TempWorkspace.Create(System.IO.Path.Combine(junction, "sub")));
        }
        finally
        {
            try { if (Directory.Exists(junction)) Directory.Delete(junction, false); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SweepStale_cleans_new_base_and_legacy_temp()
    {
        var b = NewBase();
        var legacy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SI_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var keep = TempWorkspace.Create(b);
            var old = System.IO.Path.Combine(b, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(old);
            var other = System.IO.Path.Combine(b, "notaworkspace");
            Directory.CreateDirectory(other);
            Directory.CreateDirectory(legacy);
            var past = DateTime.UtcNow.AddDays(-2);
            Directory.SetLastWriteTimeUtc(old, past);
            Directory.SetLastWriteTimeUtc(legacy, past);
            Directory.SetLastWriteTimeUtc(other, past);
            int n = TempWorkspace.SweepStale(TimeSpan.FromDays(1), b);
            Assert.True(n >= 2);
            Assert.False(Directory.Exists(old));
            Assert.False(Directory.Exists(legacy));
            Assert.True(Directory.Exists(other));
            Assert.True(Directory.Exists(keep.Path));
        }
        finally { Cleanup(b); try { Directory.Delete(legacy, true); } catch { } }
    }

    [Fact]
    public void Workspace_deleted_on_dispose_even_after_exception()
    {
        string path = "";
        try
        {
            using var ws = TempWorkspace.Create(NewBase());
            path = ws.Path;
            Assert.True(Directory.Exists(path));
            var f = System.IO.Path.Combine(path, "sub", "a.exe");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
            File.WriteAllText(f, "x");
            File.SetAttributes(f, FileAttributes.ReadOnly | FileAttributes.Hidden);
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException) { }
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Workspace_acl_is_protected_and_has_three_rules()
    {
        using var ws = TempWorkspace.Create(NewBase());
        var sec = new DirectoryInfo(ws.Path).GetAccessControl();
        Assert.True(sec.AreAccessRulesProtected);
        var rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Assert.Equal(elevated ? 2 : 3, rules.Count);
        Assert.All(rules, r =>
        {
            Assert.False(r.IsInherited);
            Assert.Equal(AccessControlType.Allow, r.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, r.FileSystemRights);
        });
        var sids = rules.Select(r => r.IdentityReference.Value).ToHashSet();
        var expected = new HashSet<string>
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
        };
        if (!elevated) expected.Add(WindowsIdentity.GetCurrent().User!.Value);
        Assert.True(expected.SetEquals(sids));
    }

    [Fact]
    public void Dispose_with_locked_file_reports_not_deleted_and_does_not_throw()
    {
        var ws = TempWorkspace.Create(NewBase());
        var f = System.IO.Path.Combine(ws.Path, "locked.exe");
        var fs = new FileStream(f, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            ws.Dispose();
            Assert.False(ws.Deleted);
        }
        finally
        {
            fs.Dispose();
            try { Directory.Delete(ws.Path, true); } catch { }
        }
    }

    [Fact]
    public void SweepStale_removes_old_workspaces_only()
    {
        var tmp = System.IO.Path.GetTempPath();
        var oldDir = System.IO.Path.Combine(tmp, "SI_" + Guid.NewGuid().ToString("N"));
        var newDir = System.IO.Path.Combine(tmp, "SI_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldDir); Directory.CreateDirectory(newDir);
        try
        {
            Directory.SetLastWriteTimeUtc(oldDir, DateTime.UtcNow.AddDays(-2));
            int n = TempWorkspace.SweepStale(TimeSpan.FromDays(1));
            Assert.True(n >= 1);
            Assert.False(Directory.Exists(oldDir));
            Assert.True(Directory.Exists(newDir));
        }
        finally { try { Directory.Delete(newDir, true); } catch { } try { Directory.Delete(oldDir, true); } catch { } }
    }

    [Fact]
    public void SweepStale_ignores_non_workspace_names()
    {
        var other = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SI_other");
        Directory.CreateDirectory(other);
        try
        {
            Directory.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-2));
            TempWorkspace.SweepStale(TimeSpan.FromDays(1));
            Assert.True(Directory.Exists(other));
        }
        finally { try { Directory.Delete(other, true); } catch { } }
    }

    [Fact]
    public void SweepStale_does_not_follow_junction_root()
    {
        var tmp = System.IO.Path.GetTempPath();
        var target = System.IO.Path.Combine(tmp, "SItarget_" + Guid.NewGuid().ToString("N"));
        var junction = System.IO.Path.Combine(tmp, "SI_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        var file = System.IO.Path.Combine(target, "keep.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
                { CreateNoWindow = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit();
            }
            catch { }
            if (!Directory.Exists(junction))
            {
                try { Directory.CreateSymbolicLink(junction, target); } catch { }
            }
            if (!Directory.Exists(junction)) return; // cannot create links here
            Directory.SetLastWriteTimeUtc(junction, DateTime.UtcNow.AddDays(-2));
            TempWorkspace.SweepStale(TimeSpan.FromDays(1));
            Assert.True(File.Exists(file));
            Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            try { if (Directory.Exists(junction)) Directory.Delete(junction, false); } catch { }
            try { File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(target, true); } catch { }
        }
    }
}
