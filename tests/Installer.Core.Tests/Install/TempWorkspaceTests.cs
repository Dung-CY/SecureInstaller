using System.Security.AccessControl;
using System.Security.Principal;
using Installer.Core.Install;

namespace Installer.Core.Tests.Install;

public class TempWorkspaceTests
{
    [Fact]
    public void Workspace_deleted_on_dispose_even_after_exception()
    {
        string path = "";
        try
        {
            using var ws = TempWorkspace.Create();
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
        using var ws = TempWorkspace.Create();
        var sec = new DirectoryInfo(ws.Path).GetAccessControl();
        Assert.True(sec.AreAccessRulesProtected);
        var rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.Equal(3, rules.Count);
        Assert.All(rules, r =>
        {
            Assert.False(r.IsInherited);
            Assert.Equal(AccessControlType.Allow, r.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, r.FileSystemRights);
        });
        var sids = rules.Select(r => r.IdentityReference.Value).ToHashSet();
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value, sids);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value, sids);
        Assert.Contains(WindowsIdentity.GetCurrent().User!.Value, sids);
    }
}
