using System.IO;
using Installer.App.ViewModels;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.App.Tests;

public class PackageEditorViewModelTests
{
    PackageDraft? _saved;
    bool _cancelled;
    string? _nextFile;
    IReadOnlyList<string> _nextFiles = Array.Empty<string>();
    Func<PackageDraft, Task>? _saveOverride;

    PackageEditorViewModel Make(PackageEntry? existing = null,
        Func<string, (string?, string?, string?)>? readInfo = null, Func<string, InstallerKind>? detect = null) =>
        new(existing, () => _nextFile, () => _nextFiles,
            d => _saveOverride?.Invoke(d) ?? Task.FromResult(_saved = d), () => _cancelled = true,
            readInfo ?? (_ => ("7-Zip 24.08 (x64 edition)", "24.08.00.0", "{23170F69-40C1-2702-2408-000001000000}")),
            detect ?? (_ => InstallerKind.Unknown));

    [Fact]
    public void Picking_an_msi_prefills_empty_fields_and_product_code()
    {
        var vm = Make();
        _nextFile = @"D:\Bản cài\7z2408-x64.msi";
        vm.PickInstallerCommand.Execute(null);

        Assert.Equal(_nextFile, vm.InstallerPath);
        Assert.Equal(InstallerType.Msi, vm.Type);
        Assert.Equal("7-Zip 24.08 (x64 edition)", vm.Name);
        Assert.Equal("24.08.00.0", vm.Version);
        Assert.Equal("7-zip-24.08-x64-edition", vm.Id);
        Assert.Equal("{23170F69-40C1-2702-2408-000001000000}", vm.ProductCode);
    }

    [Fact]
    public void Prefill_never_overwrites_what_the_admin_typed_and_exe_skips_product_code()
    {
        var vm = Make();
        vm.Name = "Trình nén"; vm.Version = "1.0"; vm.Id = "nen";
        _nextFile = @"D:\setup.EXE";
        vm.PickInstallerCommand.Execute(null);

        Assert.Equal(("Trình nén", "1.0", "nen"), (vm.Name, vm.Version, vm.Id));
        Assert.Equal(InstallerType.Exe, vm.Type);
        Assert.True(string.IsNullOrEmpty(vm.ProductCode));
    }

    [Fact]
    public void Unreadable_file_leaves_fields_for_manual_entry()
    {
        var vm = Make(readInfo: _ => (null, null, null));
        _nextFile = @"D:\tool.zip";
        vm.PickInstallerCommand.Execute(null);
        Assert.Equal(InstallerType.Zip, vm.Type);
        Assert.Equal(("", "", ""), (vm.Name, vm.Version, vm.Id));
    }

    [Theory]
    [InlineData("", "a", "1.0", true, "Nhập tên phần mềm")]
    [InlineData("A", "có dấu", "1.0", true, "Mã gói chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới")]
    [InlineData("A", "a", " ", true, "Nhập phiên bản")]
    [InlineData("A", "a", "1.0", false, "Chọn file cài")]
    public async Task Save_validates_and_shows_error(string name, string id, string version, bool installer, string error)
    {
        var vm = Make(readInfo: _ => (null, null, null));
        if (installer) { _nextFile = @"D:\a.exe"; vm.PickInstallerCommand.Execute(null); }
        vm.Name = name; vm.Id = id; vm.Version = version;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(error, vm.Error);
        Assert.Null(_saved);
    }

    [Fact]
    public async Task Save_passes_the_draft_with_extras_and_options()
    {
        var vm = Make();
        _nextFile = @"D:\7z.msi";
        vm.PickInstallerCommand.Execute(null);
        _nextFiles = new[] { @"D:\a\config.ini", @"D:\a\license.key" };
        vm.AddExtraFilesCommand.Execute(null);
        vm.AddExtraFilesCommand.Execute(null); // picking the same files again does not duplicate them
        vm.SilentArgs = "ALLUSERS=1";
        vm.TimeoutText = "600";
        vm.BlockIfWindowsCracked = true;
        vm.SelectedUninstall = vm.UninstallOptions.Single(o => o.Type == "msi");

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.Error);
        var d = Assert.IsType<PackageDraft>(_saved);
        Assert.Equal(@"D:\7z.msi", d.InstallerPath);
        Assert.Equal(new[] { @"D:\a\config.ini", @"D:\a\license.key" }, d.ExtraFiles);
        Assert.Equal(("ALLUSERS=1", 600, InstallerType.Msi), (d.SilentArgs, d.TimeoutSec, d.Type));
        Assert.Equal("{23170F69-40C1-2702-2408-000001000000}", d.Detect.ProductCode);
        Assert.Equal("block_if_windows_cracked", Assert.Single(d.Rules).Type);
        Assert.Equal("msi", d.Uninstall!.Type);
    }

    [Fact]
    public async Task Save_failure_is_shown_and_busy_clears()
    {
        var vm = Make();
        _nextFile = @"D:\7z.msi";
        vm.PickInstallerCommand.Execute(null);
        var gate = new TaskCompletionSource();
        _saveOverride = async _ => { await gate.Task; throw new InvalidDataException("Gói 7zip phiên bản 24.08 đã tồn tại"); };

        var run = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CancelCommand.CanExecute(null));
        gate.SetResult();
        await run;

        Assert.False(vm.IsBusy);
        Assert.Equal("Gói 7zip phiên bản 24.08 đã tồn tại", vm.Error);
    }

    [Fact]
    public void Editing_prefills_from_the_entry_and_asks_to_repick_the_installer()
    {
        var e = new PackageEntry("7zip", "7-Zip", "24.08", InstallerType.Msi, "7z.msi", "ALLUSERS=1", @"C:\Tools", 900,
            new DetectRule("{X}", "^7-Zip"), new(), new(), new() { new RuleSpec("require_internet") }, "b", "s", "k",
            Uninstall: new UninstallSpec("command", "uninst.exe", "/S"));
        var vm = Make(e);

        Assert.True(vm.IsEdit);
        Assert.Equal(("7zip", "7-Zip", "24.08", "ALLUSERS=1", @"C:\Tools", "900"),
            (vm.Id, vm.Name, vm.Version, vm.SilentArgs, vm.TargetDir, vm.TimeoutText));
        Assert.Equal(("{X}", "^7-Zip"), (vm.ProductCode, vm.DisplayNameRegex));
        Assert.True(vm.RequireInternet);
        Assert.Equal(("command", "uninst.exe", "/S"), (vm.SelectedUninstall.Type, vm.UninstallCommand, vm.UninstallArgs));
        Assert.Null(vm.InstallerPath);
        Assert.Contains("chọn lại file cài", vm.EditHint);
    }

    [Fact]
    public void Cancel_calls_back()
    {
        Make().CancelCommand.Execute(null);
        Assert.True(_cancelled);
    }

    [Fact]
    public void Version_prefill_keeps_only_the_numeric_part()
    {
        var vm = Make(readInfo: _ => ("Tool", "10.0.1 (build 5)", null));
        _nextFile = @"D:	ool.exe";
        vm.PickInstallerCommand.Execute(null);
        Assert.Equal("10.0.1", vm.Version);

        var vm2 = Make(readInfo: _ => ("Tool", "phiên bản mới", null));
        vm2.PickInstallerCommand.Execute(null);
        Assert.Equal("", vm2.Version);
    }

    [Fact]
    public async Task Invalid_typed_version_is_refused_without_calling_save()
    {
        var vm = Make();
        _nextFile = @"D:z.msi";
        vm.PickInstallerCommand.Execute(null);
        vm.Version = "1.0 beta";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Phiên bản chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới", vm.Error);
        Assert.Null(_saved);
    }

    [Theory]
    [InlineData("Công cụ Đồ họa", "cong-cu-do-hoa")]
    [InlineData("  7-Zip 24.08 (x64)  ", "7-zip-24.08-x64")]
    public void Slug_strips_vietnamese_marks_and_collapses_dashes(string name, string id) =>
        Assert.Equal(id, PackageEditorViewModel.Slug(name));

    [Fact]
    public async Task Edit_keeps_post_actions_and_icon_unless_dropped()
    {
        var post = new List<PostAction> { new("copy", "a.ini", @"C:.ini") };
        var e = new PackageEntry("7zip", "7-Zip", "24.08", InstallerType.Msi, "7z.msi", "", null, 900,
            new DetectRule(null, null), new() { "HuongDan.pdf" }, post, new(), "b", "s", "k", IconPngB64: "aWNvbg==");
        var vm = Make(e);
        Assert.Equal("Giữ icon hiện tại", vm.IconText);
        Assert.Contains("HuongDan.pdf", vm.EditHint);
        Assert.Contains("thao tác sau cài được giữ", vm.EditHint);
        _nextFile = @"D:z.msi";
        vm.PickInstallerCommand.Execute(null);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(post, _saved!.PostActions);
        Assert.Equal("aWNvbg==", _saved.KeepIconPngB64);

        vm.ClearIconCommand.Execute(null);
        Assert.Equal("Mặc định", vm.IconText);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(_saved.KeepIconPngB64);
    }

    [Fact]
    public async Task Inputs_are_disabled_while_saving()
    {
        var vm = Make();
        _nextFile = @"D:z.msi";
        vm.PickInstallerCommand.Execute(null);
        var gate = new TaskCompletionSource();
        _saveOverride = _ => gate.Task;
        var run = vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsIdle);
        gate.SetResult();
        await run;
        Assert.True(vm.IsIdle);
    }

    [Fact]
    public void Picking_a_detected_installer_shows_the_kind_and_prefills_silent_args()
    {
        var vm = Make(detect: _ => InstallerKind.Nsis);
        _nextFile = @"D:\Bản cài\setup.exe";
        vm.PickInstallerCommand.Execute(null);

        Assert.Equal("Đã nhận diện: NSIS", vm.KindText);
        Assert.Null(vm.KindWarning);
        Assert.Equal("/S", vm.SilentArgs);

        // Still the previous kind's default, so a different installer replaces it; a hand edit is kept.
        var next = Make(detect: _ => InstallerKind.Inno);
        next.Kind = InstallerKind.Nsis; next.SilentArgs = "/S";
        next.PickInstallerCommand.Execute(null);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", next.SilentArgs);
        vm.SilentArgs = "/S /D=X";
        vm.PickInstallerCommand.Execute(null);
        Assert.Equal("/S /D=X", vm.SilentArgs);
    }

    [Fact]
    public void Unknown_kind_warns_but_still_saves()
    {
        var vm = Make();
        _nextFile = @"D:\setup.exe";
        vm.PickInstallerCommand.Execute(null);

        Assert.Equal("Không nhận diện được loại bộ cài — hãy nhập tham số cài ngầm", vm.KindWarning);
        Assert.Null(vm.KindText);
        Assert.Equal("", vm.SilentArgs);
        vm.SaveCommand.Execute(null);
        Assert.Equal(InstallerKind.Unknown, _saved!.Kind);
    }
}
