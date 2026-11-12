using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Installer.App.ViewModels;
using Installer.Core.Install;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Scanning;
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

    Installer.Core.Install.TempWorkspace Ws() =>
        Installer.Core.Install.TempWorkspace.Create(Path.Combine(_tmp, "work"), () => false);

    VaultSession Login() => VaultStore.Open(_root).Unlock("admin", "pw-1234", T0);

    AdminViewModel Make(VaultSession s, Func<string?>? folder = null, Func<string?>? save = null,
        Func<string, bool>? confirm = null, Action? back = null, Func<string, byte[]?>? iconFromExe = null,
        Action<object>? navigate = null) =>
        new(s, _logs, _fallback, folder ?? (() => null), save ?? (() => null), confirm ?? (_ => true), back ?? (() => { }),
            iconFromExe, navigate);

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
    public async Task Export_csv_writes_file()
    {
        var s = Login();
        var log = new EncryptedLog(_root, s.Store.Meta.VaultId, s.VaultKey, false);
        log.Append(new LogEntry(DateTimeOffset.Now, "an", "MAY-01", null, "install", "demo", "1.0", "ok", 0, null));
        log.Append(new LogEntry(DateTimeOffset.Now, "binh", "MAY-02", null, "scan", null, null, "ok", null, null));
        var vm = Make(s, save: () => Path.Combine(_tmp, "out.csv"));
        await vm.InitialLoad;
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
        var pkg = new Installer.Core.Model.PackageEntry("x", "x", "1.0", Installer.Core.Model.InstallerType.Zip, "a.zip", "", null, 60,
            new Installer.Core.Model.DetectRule(null, "x"), new(), new(), new(), "b.enc", "00", "AA==");
        var inst = new QueueInstallViewModel("x", new[] { pkg }, (_, _, _) => tcs.Task, () => { });
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
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        // The exe-icon extractor runs inside the operation (on the worker), so it holds the operation open.
        var vm = Make(s, () => src, iconFromExe: _ => { entered.TrySetResult(); gate.Task.Wait(); return null; });
        await vm.InitialLoad;

        var op = vm.AddPackageCommand.ExecuteAsync(null);
        await entered.Task;
        Assert.False(op.IsCompleted);
        Assert.True(vm.IsBusy);
        Assert.Equal(AdminViewModel.BusyText, vm.BusyMessage);
        Assert.False(vm.AddPackageCommand.CanExecute(null));
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
        Assert.False(vm.SaveBundleCommand.CanExecute(null));
        var main = new MainViewModel();
        main.NavigateTo(vm);
        Assert.Equal("Đang xử lý. Vui lòng đợi trước khi thoát.", main.CloseBlockedMessage);

        gate.SetResult();
        await op;
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.BusyMessage);
        Assert.True(vm.AddPackageCommand.CanExecute(null));
        Assert.True(vm.BackCommand.CanExecute(null));
        Assert.Null(main.CloseBlockedMessage);
    }

    // ---------- Bundles ----------

    void AddPackages(VaultSession s, params (string Id, string Version)[] pkgs)
    {
        var admin = new VaultAdmin(s);
        foreach (var (id, v) in pkgs) admin.AddPackage(MakePackage(_tmp, id, v));
    }

    [Fact]
    public async Task Admin_save_bundle_parses_lines_and_persists()
    {
        var s = Login();
        AddPackages(s, ("demo", "1.0"), ("b", "1.0"), ("b", "2.0"));
        string? asked = null;
        var vm = Make(s, confirm: m => { asked = m; return true; });
        Assert.Empty(vm.Bundles);
        Assert.False(vm.RemoveBundleCommand.CanExecute(null));

        vm.EditId = "office";
        vm.EditName = "Văn phòng";
        vm.EditDescription = "Cho kế toán";
        vm.EditItems = "demo\r\n\r\n   \n  b@1.0  \n";
        await vm.SaveBundleCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        Assert.Equal("Đã lưu bộ gói Văn phòng", vm.Status);
        var row = Assert.Single(vm.Bundles);
        Assert.Equal("office", row.Id);
        Assert.Equal(2, row.ItemCount);

        var saved = Assert.Single(Login().Catalog.Bundles!);
        Assert.Equal(("office", "Văn phòng", "Cho kế toán"), (saved.Id, saved.Name, saved.Description));
        Assert.Equal(new[] { new BundleItem("demo"), new BundleItem("b", "1.0") }, saved.Items);

        // Selecting a row loads it into the editor; saving the same id updates it in place.
        vm.NewBundleCommand.Execute(null);
        Assert.Equal("", vm.EditId);
        Assert.Equal("", vm.EditItems);
        vm.SelectedBundle = vm.Bundles.Single();
        Assert.Equal("office", vm.EditId);
        Assert.Equal("Văn phòng", vm.EditName);
        Assert.Equal("Cho kế toán", vm.EditDescription);
        Assert.Equal("demo\nb@1.0", vm.EditItems);
        vm.EditName = "Văn phòng 2";
        vm.EditItems = "b";
        await vm.SaveBundleCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        var updated = Assert.Single(Login().Catalog.Bundles!);
        Assert.Equal("Văn phòng 2", updated.Name);
        Assert.Equal(new[] { new BundleItem("b") }, updated.Items);

        vm.SelectedBundle = vm.Bundles.Single();
        Assert.True(vm.RemoveBundleCommand.CanExecute(null));
        await vm.RemoveBundleCommand.ExecuteAsync(null);
        Assert.Equal("Xóa bộ gói Văn phòng 2?", asked);
        Assert.Null(vm.Error);
        Assert.Equal("Đã xóa bộ gói Văn phòng 2", vm.Status);
        Assert.Empty(vm.Bundles);
        Assert.Empty(Login().Catalog.Bundles ?? new());
    }

    [Fact]
    public async Task Admin_invalid_bundle_line_shows_error()
    {
        var s = Login();
        AddPackages(s, ("demo", "1.0"));
        var v0 = s.Catalog.Version;
        var vm = Make(s);
        vm.EditId = "office";
        vm.EditName = "Văn phòng";
        foreach (var (text, line) in new[] { ("demo\n@1.0", 2), ("a@b@c", 1), ("demo@", 1), ("\n\nbad id", 3) })
        {
            vm.EditItems = text;
            await vm.SaveBundleCommand.ExecuteAsync(null);
            Assert.Equal($"Dòng {line} không hợp lệ", vm.Error);
        }
        // Validation inside VaultAdmin is shown through ErrorText.ForUser.
        vm.EditItems = "ghost";
        await vm.SaveBundleCommand.ExecuteAsync(null);
        Assert.Equal("Bộ gói tham chiếu gói không có trong kho: ghost", vm.Error);
        vm.EditItems = "";
        await vm.SaveBundleCommand.ExecuteAsync(null);
        Assert.Equal("Bộ gói không có gói nào", vm.Error);
        Assert.Equal(v0, s.Catalog.Version);
        Assert.Empty(vm.Bundles);
    }

    // ---------- Scan history / logs ----------

    static ScanReport ScanOf(string host, DateTimeOffset at) => new(at,
        new HardwareInfo(host, "Dell", "Opti", "SN-" + host, new(), new(), new(), new()),
        new NetworkHistory(new(), true),
        new Installer.Core.Licensing.LicenseInfo("Windows", "Pro", Installer.Core.Licensing.LicenseChannel.Retail, true, null, null,
            Installer.Core.Licensing.LicenseVerdict.Genuine, new()),
        new Installer.Core.Licensing.LicenseInfo("Office", null, Installer.Core.Licensing.LicenseChannel.Unknown, null, null, null,
            Installer.Core.Licensing.LicenseVerdict.NotInstalled, new()),
        new KeywordScanResult(new(), false, false));

    static LogEntry ScanLog(string host, DateTimeOffset at) =>
        new(at, "an", host, "SN-" + host, "scan", null, null, "ok", null, "x",
            JsonSerializer.Serialize(ScanOf(host, at), Json.Options));

    [Fact]
    public async Task Scan_history_lists_filters_pages_and_opens_report()
    {
        var s = Login();
        var log = new EncryptedLog(_root, s.Store.Meta.VaultId, s.VaultKey, false);
        var day = new DateTimeOffset(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Local));
        for (var i = 0; i < 203; i++) log.Append(ScanLog("MAY-01", day.AddMinutes(i)));
        var may2a = day.AddDays(4); // 05/11 local
        log.Append(ScanLog("MAY-02", may2a));
        log.Append(ScanLog("MAY-02", day.AddDays(6)));
        log.Append(new LogEntry(day, "an", "MAY-03", null, "install", "demo", "1.0", "ok", 0, null));

        var nav = new List<object>();
        var vm = Make(s, navigate: nav.Add);
        await vm.InitialLoad;
        Assert.Equal(205, vm.HistoryCount);
        Assert.Equal(200, vm.HistoryPage.Count);
        Assert.Equal("Trang 1/2", vm.PageText);
        Assert.Equal("MAY-02", vm.HistoryPage[0].Item.Hostname); // newest first
        Assert.False(vm.PrevPageCommand.CanExecute(null));
        Assert.True(vm.NextPageCommand.CanExecute(null));
        vm.NextPageCommand.Execute(null);
        Assert.Equal("Trang 2/2", vm.PageText);
        Assert.Equal(5, vm.HistoryPage.Count);
        Assert.False(vm.NextPageCommand.CanExecute(null));
        Assert.True(vm.PrevPageCommand.CanExecute(null));

        // The Log tab's filters do not touch the history.
        vm.MachineFilter = "không có";
        vm.FromDate = vm.ToDate = new DateTime(2000, 1, 1);
        Assert.Empty(vm.FilteredLogs);
        Assert.Equal(205, vm.HistoryCount);
        Assert.Equal("Trang 2/2", vm.PageText);

        vm.HistoryMachineFilter = "may-02";
        Assert.Equal("Trang 1/1", vm.PageText);
        Assert.Equal(2, vm.HistoryPage.Count);
        vm.HistoryFrom = new DateTime(2026, 11, 5);
        vm.HistoryTo = new DateTime(2026, 11, 5);
        var row = Assert.Single(vm.HistoryPage);
        Assert.Equal(may2a, row.Item.Ts);
        vm.HistoryMachineFilter = "không có";
        Assert.Empty(vm.HistoryPage);
        Assert.Equal("Trang 1/1", vm.PageText);
        Assert.False(vm.NextPageCommand.CanExecute(null));
        vm.HistoryMachineFilter = "MAY-02";

        vm.SelectedTabIndex = AdminViewModel.HistoryTabIndex;
        vm.OpenScanCommand.Execute(row.Item);
        var report = Assert.IsType<ScanReportViewModel>(Assert.Single(nav));
        Assert.True(report.IsReadOnly);
        Assert.Same(row.Item.Report, report.Report);
        Assert.Equal("Lịch sử quét — MAY-02 — " + may2a.ToLocalTime().ToString("dd/MM/yyyy HH:mm",
            System.Globalization.CultureInfo.InvariantCulture), report.Title);
        report.ContinueCommand.Execute(null);
        Assert.Same(vm, nav[1]);
        Assert.Equal(AdminViewModel.HistoryTabIndex, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task History_filter_resets_to_page_1()
    {
        var s = Login();
        var log = new EncryptedLog(_root, s.Store.Meta.VaultId, s.VaultKey, false);
        var day = new DateTimeOffset(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Local));
        for (var i = 0; i < 410; i++) log.Append(ScanLog(i < 300 ? "MAY-A" : "MAY-B", day.AddMinutes(i)));
        var vm = Make(s);
        await vm.InitialLoad;
        Assert.Equal(410, vm.HistoryCount);
        Assert.Equal("Trang 1/3", vm.PageText);
        vm.NextPageCommand.Execute(null);
        Assert.Equal("Trang 2/3", vm.PageText);
        vm.HistoryMachineFilter = "may-a"; // 300 matches: still two pages
        Assert.Equal(300, vm.HistoryCount);
        Assert.Equal("Trang 1/2", vm.PageText);
        Assert.Equal(200, vm.HistoryPage.Count);
        Assert.False(vm.PrevPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadLogsAsync_failure_sets_error_and_clears_busy()
    {
        var s = Login();
        Directory.CreateDirectory(_logs);
        var dir = new DirectoryInfo(_logs);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var sec = dir.GetAccessControl();
        sec.AddAccessRule(rule);
        dir.SetAccessControl(sec);
        try
        {
            var vm = Make(s);
            await vm.InitialLoad;
            Assert.Equal(Installer.Core.Strings.AccessDenied, vm.Error);
            Assert.False(vm.IsLoadingLogs);
            Assert.False(vm.IsBusy);
            Assert.Empty(vm.FilteredLogs);
            Assert.Empty(vm.HistoryPage);
            Assert.True(vm.LoadLogsCommand.CanExecute(null));
        }
        finally
        {
            sec = dir.GetAccessControl();
            sec.RemoveAccessRule(rule);
            dir.SetAccessControl(sec);
        }
    }

    [Fact]
    public async Task Logs_load_async_and_clear_error_on_success()
    {
        var s = Login();
        var vm = Make(s);
        await vm.InitialLoad;
        Assert.Empty(vm.FilteredLogs);
        Assert.Empty(vm.HistoryPage);
        Assert.Equal("Trang 1/1", vm.PageText);

        var log = new EncryptedLog(_root, s.Store.Meta.VaultId, s.VaultKey, false);
        log.Append(new LogEntry(DateTimeOffset.Now, "an", "MAY-01", null, "install", "demo", "1.0", "ok", 0, null));
        log.Append(ScanLog("MAY-01", DateTimeOffset.Now));
        vm.Error = "lỗi cũ";
        await vm.LoadLogsCommand.ExecuteAsync(null);
        Assert.Null(vm.Error);
        Assert.False(vm.IsLoadingLogs);
        Assert.Equal(2, vm.FilteredLogs.Count);
        Assert.Single(vm.HistoryPage);
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

    [Fact]
    public async Task Editor_add_saves_into_vault_and_returns_to_the_packages_tab()
    {
        var picked = Path.Combine(_tmp, "Bản cài");
        Directory.CreateDirectory(picked);
        var setup = Path.Combine(picked, "setup.exe");
        File.WriteAllBytes(setup, new byte[2000]);
        var extra = Path.Combine(picked, "config.ini");
        File.WriteAllText(extra, "k=v");
        object? current = null;
        var vm = new AdminViewModel(Login(), _logs, _fallback, () => null, () => null, _ => true, () => { },
            null, o => current = o, () => setup, () => new[] { extra }, Ws);
        vm.SelectedTabIndex = 2;

        vm.NewPackageCommand.Execute(null);
        var ed = Assert.IsType<PackageEditorViewModel>(current);
        Assert.Equal(0, vm.SelectedTabIndex);
        ed.PickInstallerCommand.Execute(null);
        ed.Name = "Công cụ"; ed.Id = "congcu"; ed.Version = "2.1";
        ed.AddExtraFilesCommand.Execute(null);
        await ed.SaveCommand.ExecuteAsync(null);

        Assert.Null(ed.Error);
        Assert.Same(vm, current);
        Assert.Equal("Đã thêm gói Công cụ 2.1", vm.Status);
        var row = Assert.Single(vm.Packages);
        Assert.Equal(("congcu", "2.1"), (row.Id, row.Version));

        // Saving the same id+version again fails inside the editor, which stays open.
        vm.NewPackageCommand.Execute(null);
        var ed2 = Assert.IsType<PackageEditorViewModel>(current);
        ed2.PickInstallerCommand.Execute(null);
        ed2.Name = "Công cụ"; ed2.Id = "congcu"; ed2.Version = "2.1";
        await ed2.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Gói congcu phiên bản 2.1 đã tồn tại", ed2.Error);
        Assert.Same(ed2, current);
    }

    [Fact]
    public void Edit_opens_the_editor_prefilled_and_cancel_returns()
    {
        var s = Login();
        new VaultAdmin(s).AddPackage(MakePackage(_tmp, "demo", "1.0"));
        object? current = null;
        var vm = new AdminViewModel(s, _logs, _fallback, () => null, () => null, _ => true, () => { },
            null, o => current = o, () => null, Array.Empty<string>);
        vm.SelectedPackage = vm.Packages.Single();
        vm.EditPackageCommand.Execute(null);
        var ed = Assert.IsType<PackageEditorViewModel>(current);
        Assert.Equal(("demo", "Demo demo", "1.0", "/S"), (ed.Id, ed.Name, ed.Version, ed.SilentArgs));
        ed.CancelCommand.Execute(null);
        Assert.Same(vm, current);
    }

    [Fact]
    public async Task Editor_edit_passes_KeepOld_and_keeps_icon_and_post_actions()
    {
        var s = Login();
        var src = MakePackage(_tmp, "demo", "1.0");
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9 };
        File.WriteAllBytes(Path.Combine(src, "icon.png"), png);
        var mf = Path.Combine(src, "manifest.json");
        File.WriteAllText(mf, File.ReadAllText(mf).TrimEnd().TrimEnd('}') +
            ", \"post_actions\": [ { \"type\": \"copy\", \"src\": \"a.ini\", \"dst\": \"C:/a.ini\" } ] }");
        new VaultAdmin(s).AddPackage(src);
        var setup = Path.Combine(_tmp, "setup.exe");
        File.WriteAllBytes(setup, new byte[1500]);
        object? current = null;
        var vm = new AdminViewModel(s, _logs, _fallback, () => null, () => null, _ => true, () => { },
            null, o => current = o, () => setup, Array.Empty<string>, Ws);
        vm.SelectedPackage = vm.Packages.Single();
        vm.EditPackageCommand.Execute(null);
        var ed = Assert.IsType<PackageEditorViewModel>(current);
        ed.PickInstallerCommand.Execute(null);
        ed.Version = "2.0";
        ed.KeepOld = true;
        await ed.SaveCommand.ExecuteAsync(null);

        Assert.Null(ed.Error);
        Assert.Equal(new[] { "1.0", "2.0" }, vm.Packages.Select(p => p.Version).OrderBy(v => v));
        var v2 = s.Catalog.Packages.Single(p => p.Version == "2.0");
        Assert.Equal(Convert.ToBase64String(png), v2.IconPngB64);
        Assert.Equal("a.ini", Assert.Single(v2.PostActions).Src);
    }
}
