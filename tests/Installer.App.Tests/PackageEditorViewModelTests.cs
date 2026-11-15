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
        vm.SaveCommand.Execute(null);
        Assert.Equal("{23170F69-40C1-2702-2408-000001000000}", _saved!.Detect.ProductCode);
        Assert.Null(_saved.Detect.DisplayNameRegex);
    }

    [Fact]
    public void Picking_an_exe_sets_a_name_regex_without_the_version_token()
    {
        var vm = Make(readInfo: _ => ("7-Zip 24.08", "24.08", null));
        _nextFile = @"D:\setup.exe";
        vm.PickInstallerCommand.Execute(null);
        Assert.Equal("(?<![A-Za-z0-9])7-Zip(?![A-Za-z])", vm.DisplayNameRegex);
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
        Assert.False(string.IsNullOrEmpty(vm.DisplayNameRegex));
        vm.SaveCommand.Execute(null);
        Assert.Null(_saved!.Detect.ProductCode);
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
    [InlineData("A", "có dấu", "1.0", true, "Mã phần mềm chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới")]
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

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.Error);
        var d = Assert.IsType<PackageDraft>(_saved);
        Assert.Equal(@"D:\7z.msi", d.InstallerPath);
        Assert.Equal(new[] { @"D:\a\config.ini", @"D:\a\license.key" }, d.ExtraFiles);
        Assert.Equal(("ALLUSERS=1", 600, InstallerType.Msi), (d.SilentArgs, d.TimeoutSec, d.Type));
        Assert.Equal("{23170F69-40C1-2702-2408-000001000000}", d.Detect.ProductCode);
        Assert.Equal("block_if_windows_cracked", Assert.Single(d.Rules).Type);
        Assert.Null(d.Uninstall);
    }

    [Fact]
    public async Task Save_failure_is_shown_and_busy_clears()
    {
        var vm = Make();
        _nextFile = @"D:\7z.msi";
        vm.PickInstallerCommand.Execute(null);
        var gate = new TaskCompletionSource();
        _saveOverride = async _ => { await gate.Task; throw new InvalidDataException("Phần mềm 7zip phiên bản 24.08 đã tồn tại"); };

        var run = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CancelCommand.CanExecute(null));
        gate.SetResult();
        await run;

        Assert.False(vm.IsBusy);
        Assert.Equal("Phần mềm 7zip phiên bản 24.08 đã tồn tại", vm.Error);
    }

    [Fact]
    public void Editing_prefills_from_the_entry_and_keeps_the_installer()
    {
        var e = new PackageEntry("7zip", "7-Zip", "24.08", InstallerType.Msi, "7z.msi", "ALLUSERS=1", @"C:\Tools", 900,
            new DetectRule("{X}", "^7-Zip"), new(), new(), new() { new RuleSpec("require_internet") }, "b", "s", "k",
            Uninstall: new UninstallSpec("command", "uninst.exe", "/S"));
        var vm = Make(e);

        Assert.True(vm.IsEdit);
        Assert.Equal(("7zip", "7-Zip", "24.08", "ALLUSERS=1", @"C:\Tools", "900"),
            (vm.Id, vm.Name, vm.Version, vm.SilentArgs, vm.TargetDir, vm.TimeoutText));
        Assert.Equal("^7-Zip", vm.DisplayNameRegex);
        Assert.True(vm.RequireInternet);
        Assert.Null(vm.InstallerPath);
        Assert.Equal("7z.msi", Assert.Single(vm.ExistingFiles).Name);
        Assert.False(vm.KeepOldEnabled);
        Assert.Equal("Chọn file cài mới để giữ bản cũ", vm.KeepOldHint);
    }

    [Fact]
    public async Task Edit_round_trip_keeps_product_code_and_uninstall_spec()
    {
        var spec = new UninstallSpec("command", "uninst.exe", "/S");
        var e = new PackageEntry("7zip", "7-Zip", "24.08", InstallerType.Msi, "7z.msi", "", null, 900,
            new DetectRule("{X}", "^7-Zip"), new(), new(), new(), "b", "s", "k", Uninstall: spec);
        var vm = Make(e);
        vm.InstallerPath = @"D:z.msi";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("{X}", _saved!.Detect.ProductCode);
        Assert.Equal(spec, _saved.Uninstall);
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
        Assert.Contains(vm.ExistingFiles, r => r.Name == "HuongDan.pdf");
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

    static PackageEntry Stored() => new("p", "P", "1.0", InstallerType.Exe, "setup.exe", "/S", null, 900, new DetectRule(null, null),
        new() { "d.pdf" }, new(), new(), "b", "s", "k", PreScript: "pre.cmd", Extras: new() { "a.ini", "sub/b.txt" });

    [Fact]
    public async Task Edit_without_file_changes_keeps_everything_and_flags_metadata_only()
    {
        var vm = Make(Stored());
        vm.SilentArgs = "/VERYSILENT";
        await vm.SaveCommand.ExecuteAsync(null);
        var edit = _saved!.Edit!;
        Assert.False(edit.FilesChanged);
        Assert.Null(_saved.InstallerPath);
        Assert.Equal(("setup.exe", "pre.cmd"), (edit.Kept.Installer, edit.Kept.PreScript));
        Assert.Equal(new[] { "a.ini", "sub/b.txt" }, edit.Kept.Extras);
        Assert.Equal(new[] { "d.pdf" }, edit.Kept.Docs);
    }

    [Fact]
    public async Task Replacing_and_removing_rows_marks_files_changed()
    {
        var vm = Make(Stored());
        var a = vm.ExistingFiles.Single(r => r.Name == "a.ini");
        _nextFile = @"D:\n\a.ini";
        a.ReplaceCommand.Execute(null);
        vm.ExistingFiles.Single(r => r.Name == "d.pdf").RemoveCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        var d = _saved!;
        Assert.True(d.Edit!.FilesChanged);
        Assert.Equal(new[] { @"D:\n\a.ini" }, d.ExtraFiles);
        Assert.Equal(new[] { "sub/b.txt" }, d.Edit.Kept.Extras);
        Assert.Empty(d.Edit.Kept.Docs);
        Assert.Equal("→ a.ini", a.ReplacementName);
        a.KeepCommand.Execute(null);
        Assert.True(a.IsKeep);
    }

    [Fact]
    public async Task Keep_old_needs_a_new_installer_and_resets_when_it_is_dropped()
    {
        var vm = Make(Stored());
        _nextFile = @"D:\new.exe";
        vm.ExistingFiles[0].ReplaceCommand.Execute(null);
        Assert.True(vm.KeepOldEnabled);
        vm.KeepOld = true;
        vm.ExistingFiles[0].KeepCommand.Execute(null);
        Assert.False(vm.KeepOldEnabled);
        Assert.False(vm.KeepOld);
        Assert.Null(vm.InstallerPath);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(_saved!.Edit!.FilesChanged);
    }

    [Fact]
    public async Task Extra_folder_is_listed_with_relative_paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SI_vm_" + Guid.NewGuid().ToString("N"), "Cấu hình");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "y.cfg"), "y");
        try
        {
            var vm = new PackageEditorViewModel(Stored(), () => null, () => _nextFiles, d => Task.FromResult(_saved = d), () => { },
                pickFolder: () => dir);
            vm.AddExtraFolderCommand.Execute(null);
            vm.AddExtraFolderCommand.Execute(null); // same folder twice is ignored
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal("Cấu hình/sub/y.cfg", Assert.Single(_saved!.ExtraEntries!).Rel);
            Assert.True(_saved.Edit!.FilesChanged);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Fact]
    public void Data_copy_is_off_unless_ticked_and_needs_a_file_list()
    {
        var vm = Make(Stored());
        Assert.False(vm.DataEnabled);
        vm.SaveCommand.Execute(null);
        Assert.Null(_saved!.Data);

        vm.DataEnabled = true;
        vm.DataTargetDir = @"C:\QLVBDH\Config";
        vm.SaveCommand.Execute(null);
        Assert.Equal("Nhập ít nhất một file cần chép", vm.Error);

        vm.DataFilesText = "{DonVi}.txt => config.txt\r\n\n huongdan.pdf ";
        vm.SaveCommand.Execute(null);
        Assert.Equal(new[] { "{DonVi}.txt => config.txt", "huongdan.pdf" }, _saved!.Data!.Files);

        var again = Make(Stored() with { Data = _saved.Data });
        Assert.True(again.DataEnabled);
        Assert.Equal("{DonVi}.txt => config.txt\nhuongdan.pdf", again.DataFilesText);
        Assert.Contains(@"DuLieu\p", again.DataFolderHint);
    }

    [Fact]
    public void Display_name_is_plain_text_and_old_regexes_survive_untouched()
    {
        var vm = Make(Stored() with { Detect = new DetectRule(null, @"(?<![A-Za-z0-9])Notepad\+\+(?![A-Za-z])") });
        Assert.Equal("Notepad++", vm.DisplayNameText);
        vm.DisplayNameText = "LibreOffice ";
        Assert.Equal("(?<![A-Za-z0-9])LibreOffice(?![A-Za-z])", vm.DisplayNameRegex);
        vm.DisplayNameText = "";
        Assert.Null(vm.DisplayNameRegex);

        var old = Make(Stored() with { Detect = new DetectRule(null, "^Bit4id") });
        Assert.Equal("^Bit4id", old.DisplayNameText);
        Assert.Equal("^Bit4id", old.DisplayNameRegex);
    }
}
