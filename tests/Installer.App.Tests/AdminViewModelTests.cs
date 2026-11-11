using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Installer.App.ViewModels;
using Installer.Core.Install;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.App.Tests;

public sealed class VaultTemplateFixture : IDisposable
{
    public string Template { get; } = Path.Combine(Path.GetTempPath(), "SI_admtpl_" + Guid.NewGuid().ToString("N"), "Kho Dự án");
    public VaultTemplateFixture() => VaultStore.Create(Template, "admin", "pw-1234");
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(Template)!, true); } catch { } }
}

public class AdminViewModelTests : IClassFixture<VaultTemplateFixture>, IDisposable
{
    static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_admtest_" + Guid.NewGuid().ToString("N"));
    readonly string _root;
    readonly string _logs;
    readonly string _fallback;
    bool _denied;

    public AdminViewModelTests(VaultTemplateFixture fx)
    {
        _root = Path.Combine(_tmp, "Kho Dự án");
        _logs = Path.Combine(_root, "logs");
        _fallback = Path.Combine(_tmp, "fallback");
        CopyDir(fx.Template, _root);
    }

    public void Dispose()
    {
        try { if (_denied) RestoreAcl(); } catch { }
        try { Directory.Delete(_tmp, true); } catch { }
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static string MakePackage(string parent, string id = "demo", string version = "1.0")
    {
        var dir = Path.Combine(parent, "src_" + id + "_" + version + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(dir, "installer"));
        Directory.CreateDirectory(Path.Combine(dir, "docs"));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
        { "id": "{{id}}", "name": "Demo {{id}}", "version": "{{version}}",
          "installer_type": "exe", "installer_file": "setup.exe", "silent_args": "/S" }
        """, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(dir, "installer", "setup.exe"), Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray());
        File.WriteAllText(Path.Combine(dir, "docs", "HuongDan.docx"), "huong dan");
        return dir;
    }

    VaultSession Login() => VaultStore.Open(_root).Unlock("admin", "pw-1234", T0);

    AdminViewModel Make(VaultSession s, Func<string?>? folder = null, Func<string?>? save = null,
        Func<string, bool>? confirm = null, Action? back = null) =>
        new(s, _logs, _fallback, folder ?? (() => null), save ?? (() => null), confirm ?? (_ => true), back ?? (() => { }));

    void DenyWrites()
    {
        var dir = new DirectoryInfo(Path.Combine(_root, "vault"));
        var sec = dir.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData, AccessControlType.Deny));
        dir.SetAccessControl(sec);
        _denied = true;
    }

    void RestoreAcl()
    {
        var dir = new DirectoryInfo(Path.Combine(_root, "vault"));
        var sec = dir.GetAccessControl();
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            if (r.AccessControlType == AccessControlType.Deny) sec.RemoveAccessRule(r);
        dir.SetAccessControl(sec);
    }

    [Fact]
    public async Task Add_package_from_picked_folder_appears_in_list_and_version_bumped()
    {
        var s = Login();
        var v0 = s.Catalog.Version;
        var src = MakePackage(_tmp);
        var vm = Make(s, () => src);
        await vm.AddPackageCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        var row = Assert.Single(vm.Packages);
        Assert.Equal("demo", row.Id);
        Assert.Equal("1.0", row.Version);
        Assert.Equal(1, row.DocCount);
        Assert.Equal(v0 + 1, s.Catalog.Version);
        Assert.Equal("Đã thêm gói Demo demo 1.0", vm.Status);
    }

    [Fact]
    public async Task Cancel_picker_does_nothing()
    {
        var s = Login();
        var v0 = s.Catalog.Version;
        var vm = Make(s, () => null);
        await vm.AddPackageCommand.ExecuteAsync(null);
        Assert.Empty(vm.Packages);
        Assert.Equal(v0, s.Catalog.Version);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Remove_last_admin_shows_error()
    {
        var vm = Make(Login());
        vm.SelectedUser = vm.Users.Single();
        await vm.RemoveUserCommand.ExecuteAsync(null);
        Assert.Equal("Không thể xóa quản trị viên cuối cùng", vm.Error);
        Assert.Single(vm.Users);
    }

    [Fact]
    public async Task Save_settings_persists_after_reopen()
    {
        var s = Login();
        var vm = Make(s);
        vm.KeywordsText = "hợp đồng\nbáo giá";
        vm.PathsText = "C:\\Dữ liệu";
        vm.MaxSecondsText = "120";
        vm.SignaturesText = "service|^Kms.*\nfile|crack\\.exe";
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);

        var s2 = Login();
        Assert.Equal(new[] { "hợp đồng", "báo giá" }, s2.Catalog.KeywordScan.Keywords);
        Assert.Equal(new[] { "C:\\Dữ liệu" }, s2.Catalog.KeywordScan.Paths);
        Assert.Equal(120, s2.Catalog.KeywordScan.MaxSeconds);
        Assert.Equal(new[] { new CrackSignature("service", "^Kms.*"), new CrackSignature("file", "crack\\.exe") },
            s2.Catalog.CrackSignatures);
    }

    [Fact]
    public async Task Settings_invalid_signature_line_shows_error()
    {
        var s = Login();
        var v0 = s.Catalog.Version;
        var vm = Make(s);
        vm.SignaturesText = "service|ok\nbogus|x";
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal("Dòng 2 không hợp lệ", vm.Error);
        vm.SignaturesText = "file|([unclosed";
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal("Dòng 1 không hợp lệ", vm.Error);
        vm.SignaturesText = "";
        vm.MaxSecondsText = "0";
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal("Số giây phải từ 1 đến 3600", vm.Error);
        Assert.Equal(v0, s.Catalog.Version);
    }

    [Fact]
    public void Readonly_vault_disables_mutations()
    {
        DenyWrites();
        var vm = Make(Login());
        Assert.True(vm.IsReadOnly);
        Assert.Equal("Kho đang ở chế độ chỉ đọc", AdminViewModel.ReadOnlyMessage);
        Assert.False(vm.AddPackageCommand.CanExecute(null));
        Assert.False(vm.UpdatePackageCommand.CanExecute(null));
        Assert.False(vm.RemovePackageCommand.CanExecute(null));
        Assert.False(vm.AddUserCommand.CanExecute(null));
        Assert.False(vm.RemoveUserCommand.CanExecute(null));
        Assert.False(vm.ResetPasswordCommand.CanExecute(null));
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
        Assert.True(vm.ExportCsvCommand.CanExecute(null));
    }

    [Fact]
    public void Export_csv_writes_file()
    {
        var s = Login();
        var log = new EncryptedLog(_root, s.Store.Meta.VaultId, s.VaultKey, false);
        log.Append(new LogEntry(DateTimeOffset.Now, "an", "MAY-01", null, "install", "demo", "1.0", "ok", 0, null));
        log.Append(new LogEntry(DateTimeOffset.Now, "binh", "MAY-02", null, "scan", null, null, "ok", null, null));
        var vm = Make(s, save: () => Path.Combine(_tmp, "out.csv"));
        Assert.Equal(2, vm.FilteredLogs.Count);
        vm.UserFilter = "AN";
        Assert.Single(vm.FilteredLogs);
        vm.ExportCsvCommand.Execute(null);
        Assert.Null(vm.Error);
        var text = File.ReadAllText(Path.Combine(_tmp, "out.csv"));
        Assert.Contains("MAY-01", text);
        Assert.DoesNotContain("MAY-02", text);
    }

    [Fact]
    public async Task Users_role_shown_from_signing_key_not_role_field()
    {
        var s = Login();
        // Rewrite vault.meta so the Role field lies (User) while the signing key is still wrapped.
        var metaPath = Path.Combine(_root, "vault", "vault.meta");
        var meta = JsonSerializer.Deserialize<VaultMeta>(File.ReadAllBytes(metaPath), Json.Options)!;
        meta = meta with { Users = meta.Users.Select(u => u with { Role = UserRole.User }).ToList() };
        File.WriteAllBytes(metaPath, JsonSerializer.SerializeToUtf8Bytes(meta, Json.Options));
        s = Login();

        var vm = Make(s);
        Assert.Equal("Quản trị", vm.Users.Single().RoleText);
        vm.NewUsername = "an"; vm.NewPassword = "pw-an-1"; vm.NewRole = UserRole.User;
        await vm.AddUserCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        Assert.Equal("Người dùng", vm.Users.Single(x => x.Username == "an").RoleText);
        Assert.Equal("", vm.NewPassword);
    }

    [Fact]
    public async Task Commit_pending_shows_reopen_message()
    {
        var s = Login();
        var sigPath = Path.Combine(_root, "vault", "catalog.sig");
        File.SetAttributes(sigPath, FileAttributes.ReadOnly);
        try
        {
            var src = MakePackage(_tmp);
            var vm = Make(s, () => src);
            await vm.AddPackageCommand.ExecuteAsync(null);
            Assert.Equal("Thay đổi đã ghi một phần; hãy mở lại ứng dụng để hoàn tất.", vm.Error);
        }
        finally { File.SetAttributes(sigPath, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Remove_package_confirms_and_removes()
    {
        var s = Login();
        var src = MakePackage(_tmp);
        string? asked = null;
        var vm = Make(s, () => src, confirm: m => { asked = m; return true; });
        await vm.AddPackageCommand.ExecuteAsync(null);
        vm.SelectedPackage = vm.Packages.Single();
        await vm.RemovePackageCommand.ExecuteAsync(null);
        Assert.Equal("Xóa gói Demo demo 1.0?", asked);
        Assert.Empty(vm.Packages);
    }

    [Fact]
    public async Task Closing_guard_blocks_only_while_install_running()
    {
        var main = new MainViewModel();
        Assert.Null(main.CloseBlockedMessage);
        var tcs = new TaskCompletionSource<InstallOutcome>();
        var inst = new InstallViewModel("x", (_, _) => tcs.Task, () => { });
        main.NavigateTo(inst);
        Assert.Equal("Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.", main.CloseBlockedMessage);
        tcs.SetResult(new(InstallStatus.Success, 0, "ok"));
        await inst.Completion;
        Assert.Null(main.CloseBlockedMessage);
    }

    [Fact]
    public void Non_admin_session_cannot_open_admin()
    {
        new VaultAdmin(Login()).AddUser("an", "pw-an-1", UserRole.User);
        var user = VaultStore.Open(_root).Unlock("an", "pw-an-1", T0);
        Assert.False(user.IsAdmin);
        Assert.Throws<NotAdminException>(() => Make(user));
    }

    [Fact]
    public async Task Busy_disables_commands_during_operation()
    {
        var s = Login();
        var src = MakePackage(_tmp);
        var vm = Make(s, () => src);
        var seen = new List<bool>();
        vm.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(AdminViewModel.IsBusy) && vm.IsBusy)
            {
                seen.Add(vm.AddPackageCommand.CanExecute(null));
                seen.Add(vm.BackCommand.CanExecute(null));
                seen.Add(vm.SaveSettingsCommand.CanExecute(null));
                var main = new MainViewModel();
                main.NavigateTo(vm);
                Assert.Equal("Đang xử lý. Vui lòng đợi trước khi thoát.", main.CloseBlockedMessage);
                Assert.Equal(AdminViewModel.BusyText, vm.BusyMessage);
            }
        };
        await vm.AddPackageCommand.ExecuteAsync(null);
        Assert.Equal(new[] { false, false, false }, seen);
        Assert.False(vm.IsBusy);
        Assert.True(vm.AddPackageCommand.CanExecute(null));
        Assert.True(vm.BackCommand.CanExecute(null));
    }

    [Fact]
    public void Update_and_remove_need_selection()
    {
        var vm = Make(Login());
        Assert.False(vm.UpdatePackageCommand.CanExecute(null));
        Assert.False(vm.RemovePackageCommand.CanExecute(null));
        Assert.False(vm.RemoveUserCommand.CanExecute(null));
        Assert.False(vm.ResetPasswordCommand.CanExecute(null));
        vm.SelectedUser = vm.Users.Single();
        Assert.True(vm.RemoveUserCommand.CanExecute(null));
        Assert.True(vm.ResetPasswordCommand.CanExecute(null));
    }
}
