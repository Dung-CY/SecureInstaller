using System.IO;
using Installer.App.ViewModels;
using Installer.Core.Install;
using Installer.Core.Licensing;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.Tests;

public class PackageListViewModelTests
{
    static LicenseInfo Lic(LicenseVerdict v) =>
        new("Windows", "Pro", LicenseChannel.Retail, true, null, null, v, new());

    static ScanReport Report(LicenseVerdict win = LicenseVerdict.Genuine) => new(DateTimeOffset.Now,
        new HardwareInfo("PC1", "Dell", "Opti", "SN1", new(), new(), new(), new()),
        new NetworkHistory(new(), true), Lic(win), Lic(LicenseVerdict.Genuine),
        new KeywordScanResult(new(), false, false));

    static PackageEntry Pkg(string name, string ver, string[]? rules = null, string[]? docs = null) =>
        new("id-" + name + ver, name, ver, InstallerType.Zip, "a.zip", "", null, 60,
            new DetectRule(null, "^" + name + "$"), (docs ?? Array.Empty<string>()).ToList(), new(),
            (rules ?? Array.Empty<string>()).Select(r => new RuleSpec(r)).ToList(), "b.enc", "00", "AA==");

    static Catalog Cat(params PackageEntry[] p) =>
        Catalog.Empty() with { Packages = p.ToList() };

    static IReadOnlyList<InstalledApp> Apps(string name, string? ver) =>
        ver is null ? new List<InstalledApp>() : new List<InstalledApp> { new(name, ver, "k", true, false) };

    static InstallOutcome Ok => new(InstallStatus.Success, 0, "Cài đặt thành công");

    sealed class Harness
    {
        public List<object> Nav = new();
        public List<string> Confirms = new();
        public bool ConfirmAnswer = true;
        public List<string> Opened = new();
        public int Installs;
        public PackageListViewModel Vm = null!;
        public Func<PackageEntry, string?> Installed = p => null;
        public Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> Install = null!;
        public Func<PackageEntry, string, CancellationToken, Task<string>> Extract = (p, d, c) => Task.FromResult(@"C:\x\" + d);

        public static Harness Create(Catalog cat, ScanReport scan, Action<Harness>? tweak = null)
        {
            var h = new Harness();
            h.Install = (p, pr, ct) => { h.Installs++; return Task.FromResult(Ok); };
            tweak?.Invoke(h);
            var byName = cat.Packages.ToDictionary(p => p.Detect.DisplayNameRegex!.Trim('^', '$') + p.Version);
            h.Vm = new PackageListViewModel(cat, scan,
                rule => Apps(rule.DisplayNameRegex!, h.Installed(cat.Packages.First(p => p.Detect == rule))),
                (p, pr, ct) => h.Install(p, pr, ct),
                (p, d, ct) => h.Extract(p, d, ct),
                msg => { h.Confirms.Add(msg); return h.ConfirmAnswer; },
                path => h.Opened.Add(path),
                vm => h.Nav.Add(vm));
            h.Vm.CreateInstallVm = (t, run, back) =>
                new InstallViewModel(t, run, back, a => new SyncProgress(a));
            return h;
        }
    }

    sealed class SyncProgress : IProgress<InstallProgress>
    {
        readonly Action<InstallProgress> _a;
        public SyncProgress(Action<InstallProgress> a) => _a = a;
        public void Report(InstallProgress v) => _a(v);
    }

    [Fact]
    public void Rows_show_older_status_and_versions()
    {
        var p = Pkg("7-Zip", "24.08");
        var h = Harness.Create(Cat(p), Report(), x => x.Installed = _ => "22.01");
        var row = Assert.Single(h.Vm.Rows);
        Assert.Equal("7-Zip", row.Name);
        Assert.Equal("24.08", row.PackageVersion);
        Assert.Equal("22.01", row.InstalledVersion);
        Assert.Equal("Cũ hơn", row.StatusText);
        Assert.True(row.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void Rows_sorted_by_name_then_version_descending()
    {
        var h = Harness.Create(Cat(Pkg("b", "1.0"), Pkg("A", "9.0"), Pkg("A", "10.0")), Report());
        Assert.Equal(new[] { "A 10.0", "A 9.0", "b 1.0" }, h.Vm.Rows.Select(r => r.Name + " " + r.PackageVersion));
    }

    [Fact]
    public void Blocked_row_disables_install_and_lists_reasons()
    {
        var p = Pkg("Foo", "1.0", new[] { "block_if_windows_cracked", "block_if_keyword_file" });
        var scan = Report(LicenseVerdict.CrackSuspected) with
        {
            Keywords = new KeywordScanResult(new() { @"C:\crack.exe" }, false, false)
        };
        var h = Harness.Create(Cat(p), scan);
        var row = h.Vm.Rows[0];
        Assert.False(row.Allowed);
        Assert.False(row.InstallCommand.CanExecute(null));
        Assert.Contains("; ", row.ReasonsText);
        Assert.Equal(2, row.ReasonsText.Split("; ").Length);
    }

    [Fact]
    public void Downgrade_requires_confirm_and_cancel_skips_install()
    {
        var p = Pkg("Foo", "1.0");
        var h = Harness.Create(Cat(p), Report(), x => { x.Installed = _ => "2.0"; x.ConfirmAnswer = false; });
        var row = h.Vm.Rows[0];
        Assert.True(row.NeedsDowngradeConfirm);
        row.InstallCommand.Execute(null);
        Assert.Equal("Máy đang có phiên bản mới hơn (2.0). Vẫn cài Foo 1.0?", Assert.Single(h.Confirms));
        Assert.Empty(h.Nav);
        Assert.Equal(0, h.Installs);
        Assert.False(h.Vm.IsInstalling);

        h.ConfirmAnswer = true;
        row.InstallCommand.Execute(null);
        Assert.IsType<InstallViewModel>(Assert.Single(h.Nav));
        Assert.Equal(1, h.Installs);
    }

    [Fact]
    public async Task Install_progress_updates_percent_and_step()
    {
        var p = Pkg("Foo", "1.0");
        var gate = new TaskCompletionSource();
        var h = Harness.Create(Cat(p), Report(), x => x.Install = async (pk, pr, ct) =>
        {
            pr.Report(new InstallProgress(null, "Đang giải mã"));
            pr.Report(new InstallProgress(40, "Đang cài"));
            await gate.Task;
            return Ok;
        });
        h.Vm.Rows[0].InstallCommand.Execute(null);
        var ivm = Assert.IsType<InstallViewModel>(h.Nav[0]);
        Assert.Equal(40, ivm.Percent);
        Assert.False(ivm.IsIndeterminate);
        Assert.Equal("Đang cài", ivm.Step);
        Assert.False(ivm.IsDone);
        Assert.True(ivm.CancelCommand.CanExecute(null));
        Assert.False(ivm.BackCommand.CanExecute(null));
        gate.SetResult();
        await ivm.Completion;
        Assert.True(ivm.IsDone);
        Assert.True(ivm.IsSuccess);
        Assert.Equal("Cài đặt thành công", ivm.Result);
        Assert.True(ivm.BackCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reboot_required_shows_reboot_text()
    {
        var h = Harness.Create(Cat(Pkg("Foo", "1.0")), Report(), x =>
            x.Install = (p, pr, ct) => Task.FromResult(new InstallOutcome(InstallStatus.RebootRequired, 3010, "Xong")));
        h.Vm.Rows[0].InstallCommand.Execute(null);
        var ivm = (InstallViewModel)h.Nav[0];
        await ivm.Completion;
        Assert.Equal("Xong\nHãy khởi động lại máy để hoàn tất.", ivm.Result);
        Assert.True(ivm.IsSuccess);
    }

    [Fact]
    public async Task OpenDoc_calls_openFile_with_extracted_path()
    {
        var p = Pkg("Foo", "1.0", docs: new[] { "guide.pdf" });
        var h = Harness.Create(Cat(p), Report());
        var row = h.Vm.Rows[0];
        Assert.True(row.HasDocs);
        await row.OpenDocCommand.ExecuteAsync("guide.pdf");
        Assert.Equal(@"C:\x\guide.pdf", Assert.Single(h.Opened));
        Assert.Null(h.Vm.Error);
    }

    [Fact]
    public async Task OpenDoc_failure_sets_error()
    {
        var p = Pkg("Foo", "1.0", docs: new[] { "guide.pdf" });
        var h = Harness.Create(Cat(p), Report(), x => x.Extract = (_, _, _) => throw new IOException("hỏng"));
        await h.Vm.Rows[0].OpenDocCommand.ExecuteAsync("guide.pdf");
        Assert.Equal("Không mở được tài liệu: hỏng", h.Vm.Error);
        Assert.Empty(h.Opened);
    }

    [Fact]
    public async Task After_install_row_rechecked()
    {
        var p = Pkg("Foo", "24.08");
        string? installed = "22.01";
        var h = Harness.Create(Cat(p), Report(), x =>
        {
            x.Installed = _ => installed;
            x.Install = (pk, pr, ct) => { installed = "24.08"; return Task.FromResult(Ok); };
        });
        var row = h.Vm.Rows[0];
        Assert.Equal("Cũ hơn", row.StatusText);
        row.InstallCommand.Execute(null);
        var ivm = (InstallViewModel)h.Nav[0];
        await ivm.Completion;
        ivm.BackCommand.Execute(null);
        Assert.Same(h.Vm, h.Nav[1]);
        Assert.Equal("Bằng nhau", row.StatusText);
        Assert.Equal("24.08", row.InstalledVersion);
    }

    [Fact]
    public async Task Install_disabled_while_another_install_runs()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("B", "1.0")), Report(), x => x.Install = (p, pr, ct) => gate.Task);
        var (a, b) = (h.Vm.Rows[0], h.Vm.Rows[1]);
        Assert.True(b.InstallCommand.CanExecute(null));
        a.InstallCommand.Execute(null);
        Assert.True(h.Vm.IsInstalling);
        Assert.False(a.InstallCommand.CanExecute(null));
        Assert.False(b.InstallCommand.CanExecute(null));
        gate.SetResult(Ok);
        var ivm = (InstallViewModel)h.Nav[0];
        await ivm.Completion;
        Assert.False(b.InstallCommand.CanExecute(null)); // still on install screen until Back
        ivm.BackCommand.Execute(null);
        Assert.False(h.Vm.IsInstalling);
        Assert.True(b.InstallCommand.CanExecute(null));
    }
}
