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

    static PackageEntry Pkg(string name, string ver, string[]? rules = null, string[]? docs = null, string? icon = null) =>
        new("id-" + name + ver, name, ver, InstallerType.Zip, "a.zip", "", null, 60,
            new DetectRule(null, "^" + name + "$"), (docs ?? Array.Empty<string>()).ToList(), new(),
            (rules ?? Array.Empty<string>()).Select(r => new RuleSpec(r)).ToList(), "b.enc", "00", "AA==",
            IconPngB64: icon);

    static Catalog Cat(params PackageEntry[] p) =>
        Catalog.Empty() with { Packages = p.ToList() };

    static IReadOnlyList<InstalledApp> Apps(string name, string? ver) =>
        ver is null ? new List<InstalledApp>() : new List<InstalledApp> { new(name, ver, "k", true, false) };

    static InstallOutcome Ok => new(InstallStatus.Success, 0, "Cài đặt thành công");

    static readonly string[] BlockRules = { "block_if_windows_cracked" };

    sealed class Harness
    {
        public List<object> Nav = new();
        public List<string> Confirms = new();
        public Func<string, bool> Confirm = _ => true;
        public List<string> Opened = new();
        public List<string> Installs = new();
        public List<(string Pkg, int Matches)> Uninstalls = new();
        public PackageListViewModel Vm = null!;
        public Func<PackageEntry, string?> Installed = p => null;
        public Func<PackageEntry, IReadOnlyList<InstalledApp>>? InstalledApps;
        public Func<PackageEntry, IReadOnlyList<InstalledApp>, bool> CanUninstall = (p, m) => true;
        public Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> Install = null!;
        public Func<PackageEntry, string, CancellationToken, Task<string>> Extract = (p, d, c) => Task.FromResult(@"C:\x\" + d);

        public static Harness Create(Catalog cat, ScanReport scan, Action<Harness>? tweak = null)
        {
            var h = new Harness();
            h.Install = (p, pr, ct) => { h.Installs.Add(p.Name); return Task.FromResult(Ok); };
            tweak?.Invoke(h);
            h.Vm = new PackageListViewModel(cat, scan,
                p => h.InstalledApps?.Invoke(p) ?? Apps(p.Name, h.Installed(p)),
                (p, pr, ct) => h.Install(p, pr, ct),
                (p, d, ct) => h.Extract(p, d, ct),
                msg => { h.Confirms.Add(msg); return h.Confirm(msg); },
                path => h.Opened.Add(path),
                vm => h.Nav.Add(vm),
                (p, m, pr, ct) => { h.Uninstalls.Add((p.Name, m.Count)); return Task.FromResult(new InstallOutcome(InstallStatus.Success, 0, "Đã gỡ cài đặt")); },
                (p, m) => h.CanUninstall(p, m));
            h.Vm.CreateQueueVm = (t, items, run, back, running) =>
                new QueueInstallViewModel(t, items, run, back, a => new QueueInstallViewModelTests.SyncProgress(a), running);
            return h;
        }
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
        Assert.Equal("Cần cập nhật", row.StatusText);
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
        Assert.Equal(2, row.ReasonsText.Split("; ").Length);
    }

    [Fact]
    public void Blocked_row_cannot_be_selected()
    {
        var h = Harness.Create(Cat(Pkg("Foo", "1.0", BlockRules), Pkg("Bar", "1.0")), Report(LicenseVerdict.CrackSuspected));
        var (bar, foo) = (h.Vm.Rows[0], h.Vm.Rows[1]);
        foo.IsSelected = true;
        Assert.False(foo.IsSelected);
        Assert.Equal(0, h.Vm.SelectedCount);
        Assert.False(h.Vm.InstallSelectedCommand.CanExecute(null));
        Assert.Equal("Cài các mục đã chọn (0)", h.Vm.InstallSelectedLabel);

        bar.IsSelected = true;
        Assert.Equal(1, h.Vm.SelectedCount);
        Assert.Equal("Cài các mục đã chọn (1)", h.Vm.InstallSelectedLabel);
        Assert.True(h.Vm.InstallSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Install_selected_runs_in_display_order_then_back_refreshes_and_clears()
    {
        string? cInstalled = null;
        var h = Harness.Create(Cat(Pkg("C", "1.0"), Pkg("A", "1.0"), Pkg("B", "1.0")), Report(), x =>
        {
            x.Installed = p => p.Name == "C" ? cInstalled : null;
            x.Install = (p, pr, ct) => { x.Installs.Add(p.Name); if (p.Name == "C") cInstalled = "1.0"; return Task.FromResult(Ok); };
        });
        h.Vm.Rows[2].IsSelected = true; // C
        h.Vm.Rows[0].IsSelected = true; // A
        h.Vm.InstallSelectedCommand.Execute(null);
        var q = Assert.IsType<QueueInstallViewModel>(Assert.Single(h.Nav));
        await q.Completion;
        Assert.Equal(new[] { "A", "C" }, h.Installs);
        Assert.Equal(new[] { "Thành công", "Thành công" }, q.Items.Select(i => i.Status));
        Assert.True(h.Vm.IsInstalling);
        q.BackCommand.Execute(null);
        Assert.Same(h.Vm, h.Nav[1]);
        Assert.False(h.Vm.IsInstalling);
        Assert.Equal(0, h.Vm.SelectedCount);
        Assert.All(h.Vm.Rows, r => Assert.False(r.IsSelected));
        Assert.Equal("Đã cài", h.Vm.Rows[2].StatusText);
    }

    [Fact]
    public void Downgrade_requires_confirm_and_cancel_skips_install()
    {
        var p = Pkg("Foo", "1.0");
        var h = Harness.Create(Cat(p), Report(), x => { x.Installed = _ => "2.0"; x.Confirm = _ => false; });
        var row = h.Vm.Rows[0];
        Assert.True(row.NeedsDowngradeConfirm);
        row.InstallCommand.Execute(null);
        Assert.Equal("Máy đang có phiên bản mới hơn (2.0). Vẫn cài Foo 1.0?", Assert.Single(h.Confirms));
        Assert.Empty(h.Nav);
        Assert.Empty(h.Installs);
        Assert.False(h.Vm.IsInstalling);

        h.Confirm = _ => true;
        row.InstallCommand.Execute(null);
        Assert.IsType<QueueInstallViewModel>(Assert.Single(h.Nav));
        Assert.Equal(new[] { "Foo" }, h.Installs);
    }

    [Fact]
    public void Downgrade_confirms_are_asked_once_and_declined_rows_dropped()
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("B", "1.0"), Pkg("C", "1.0")), Report(), x =>
        {
            x.Installed = p => p.Name is "A" or "C" ? "5.0" : null;
            x.Confirm = _ => false;
        });
        foreach (var r in h.Vm.Rows) r.IsSelected = true;
        h.Vm.InstallSelectedCommand.Execute(null);
        var msg = Assert.Single(h.Confirms);
        Assert.Contains("A 1.0", msg);
        Assert.Contains("C 1.0", msg);
        Assert.DoesNotContain("B 1.0", msg);
        var q = Assert.IsType<QueueInstallViewModel>(Assert.Single(h.Nav));
        Assert.Equal(new[] { "B" }, q.Items.Select(i => i.Package.Name));
        Assert.Equal(new[] { "B" }, h.Installs);
    }

    [Fact]
    public void Declining_all_downgrades_starts_nothing()
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("C", "1.0")), Report(), x =>
        {
            x.Installed = _ => "5.0";
            x.Confirm = _ => false;
        });
        foreach (var r in h.Vm.Rows) r.IsSelected = true;
        h.Vm.InstallSelectedCommand.Execute(null);
        Assert.Single(h.Confirms);
        Assert.Empty(h.Nav);
        Assert.False(h.Vm.IsInstalling);
        Assert.Equal(2, h.Vm.SelectedCount); // selection kept so the user can adjust it
    }

    [Fact]
    public void Installed_version_lists_x64_and_x86()
    {
        var h = Harness.Create(Cat(Pkg("7-Zip", "24.08")), Report(), x => x.InstalledApps = p => new List<InstalledApp>
        {
            new("7-Zip", "22.01", "k32", false, false),
            new("7-Zip", "23.01", "k64", true, false),
            new("7-Zip", "bản thử", "kx", true, false), // not comparable: not listed
        });
        var row = h.Vm.Rows[0];
        Assert.Equal("23.01 (x64), 22.01 (x86)", row.InstalledVersion);
        Assert.Equal("Cần cập nhật", row.StatusText);
    }

    [Fact]
    public void Uninstall_hidden_when_cannot_and_runs_queue_when_can()
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("B", "1.0"), Pkg("C", "1.0")), Report(), x =>
        {
            x.Installed = p => p.Name == "C" ? null : "0.9";
            x.CanUninstall = (p, m) => p.Name == "A";
        });
        var (a, b, c) = (h.Vm.Rows[0], h.Vm.Rows[1], h.Vm.Rows[2]);
        Assert.True(a.CanUninstall);
        Assert.True(a.IsInstalled);
        Assert.False(b.CanUninstall);
        Assert.True(b.IsInstalled);
        Assert.Equal(Installer.Core.Strings.NoUninstall, b.UninstallToolTip);
        Assert.False(b.UninstallCommand.CanExecute(null));
        Assert.False(c.IsInstalled);
        Assert.False(c.CanUninstall);
        Assert.False(c.UninstallCommand.CanExecute(null));

        h.Confirm = _ => false;
        a.UninstallCommand.Execute(null);
        Assert.Equal("Gỡ A 0.9 khỏi máy?\n\nChương trình trên máy: A", Assert.Single(h.Confirms));
        Assert.Empty(h.Nav);

        h.Confirm = _ => true;
        a.UninstallCommand.Execute(null);
        var q = Assert.IsType<QueueInstallViewModel>(Assert.Single(h.Nav));
        Assert.Equal("Gỡ cài đặt", q.Title);
        Assert.Equal(new[] { ("A", 1) }, h.Uninstalls);
        Assert.Equal("Thành công", q.Items[0].Status);
    }

    [Fact]
    public async Task Uninstall_running_text_is_dang_go()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var h = Harness.Create(Cat(Pkg("A", "1.0")), Report(), x => x.Installed = _ => "1.0");
        h.Vm = new PackageListViewModel(Cat(Pkg("A", "1.0")), Report(), p => Apps("A", "1.0"),
            (p, pr, ct) => Task.FromResult(Ok), (p, d, ct) => Task.FromResult(""), _ => true, _ => { }, h.Nav.Add,
            (p, m, pr, ct) => gate.Task, (p, m) => true);
        h.Vm.CreateQueueVm = (t, items, run, back, running) =>
            new QueueInstallViewModel(t, items, run, back, a => new QueueInstallViewModelTests.SyncProgress(a), running);
        h.Vm.Rows[0].UninstallCommand.Execute(null);
        var q = (QueueInstallViewModel)h.Nav[0];
        Assert.Equal("Đang gỡ", q.Items[0].Status);
        gate.SetResult(new(InstallStatus.Success, 0, "ok"));
        await q.Completion;
    }

    [Fact]
    public void View_scan_and_back_keeps_selection()
    {
        var scan = Report();
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("B", "1.0")), scan);
        h.Vm.Rows[1].IsSelected = true;
        h.Vm.ViewScanCommand.Execute(null);
        var report = Assert.IsType<ScanReportViewModel>(Assert.Single(h.Nav));
        Assert.True(report.IsReadOnly);
        Assert.Equal("Báo cáo quét", report.Title);
        Assert.Same(scan, report.Report);
        report.ContinueCommand.Execute(null);
        Assert.Same(h.Vm, h.Nav[1]);
        Assert.True(h.Vm.Rows[1].IsSelected);
        Assert.Equal(1, h.Vm.SelectedCount);
    }

    [Fact]
    public void Icons_decode_base64_and_tolerate_bad_data()
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0", icon: Convert.ToBase64String(new byte[] { 9, 8 })),
            Pkg("B", "1.0", icon: "%%not base64%%"), Pkg("C", "1.0")), Report());
        Assert.Equal(new byte[] { 9, 8 }, h.Vm.Rows[0].Icon);
        Assert.Null(h.Vm.Rows[1].Icon);
        Assert.Null(h.Vm.Rows[2].Icon);
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
        Assert.Equal("Không mở được tài liệu: " + Installer.Core.Strings.IoError, h.Vm.Error);
        Assert.Empty(h.Opened);
    }

    static Catalog BundleCat(out Bundle bundle)
    {
        bundle = new Bundle("office", "Văn phòng", "Bộ cho phòng kế toán", new()
        {
            new BundleItem("id-C1.0"), new BundleItem("id-Foo1.0"), new BundleItem("ghost"), new BundleItem("ID-A1.0", "1.0"),
        });
        return Cat(Pkg("A", "1.0"), Pkg("B", "1.0"), Pkg("C", "1.0"), Pkg("Foo", "1.0", BlockRules)) with
        {
            Bundles = new() { bundle }
        };
    }

    [Fact]
    public void Selecting_bundle_ticks_only_notinstalled_or_older_and_lists_the_rest()
    {
        // A not installed, B untouched, C older, Foo blocked; "ghost" missing
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected), x => x.Installed = p => p.Name == "C" ? "0.5" : null);
        Assert.True(h.Vm.HasBundles);
        Assert.Same(bundle, Assert.Single(h.Vm.Bundles));
        h.Vm.Rows.Single(r => r.Name == "B").IsSelected = true; // cleared by applying the bundle

        h.Vm.SelectedBundle = bundle;

        Assert.Equal(new[] { "A", "C" }, h.Vm.Rows.Where(r => r.IsSelected).Select(r => r.Name));
        var foo = h.Vm.Rows.Single(r => r.Name == "Foo");
        Assert.Equal(new[] { $"Foo: {foo.ReasonsText}", "ghost: Không có trong kho" }, h.Vm.BundleNotices);
        h.Vm.InstallSelectedCommand.Execute(null);
        Assert.Equal(new[] { "C", "A" }, h.Installs); // bundle order, not display order
    }

    [Fact]
    public void Bundle_skips_installed_rows_with_notice()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected), x => x.Installed = p => p.Name == "A" ? "1.0" : null);
        h.Vm.SelectedBundle = bundle;
        Assert.Equal(new[] { "C" }, h.Vm.Rows.Where(r => r.IsSelected).Select(r => r.Name));
        Assert.Contains("A: đã cài trên máy", h.Vm.BundleNotices);
    }

    [Fact]
    public void Manual_tick_after_bundle_falls_back_to_display_order()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected));
        h.Vm.SelectedBundle = bundle;
        h.Vm.Rows.Single(r => r.Name == "B").IsSelected = true;
        h.Vm.InstallSelectedCommand.Execute(null);
        Assert.Equal(new[] { "A", "B", "C" }, h.Installs);
    }

    [Fact]
    public void Null_bundle_clears_notices_but_keeps_ticks()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected));
        h.Vm.SelectedBundle = bundle;
        h.Vm.SelectedBundle = null;
        Assert.False(h.Vm.HasBundleNotices);
        Assert.Equal(2, h.Vm.SelectedCount);
    }

    [Fact]
    public void ClearSelection_unticks_all_and_resets_bundle()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected));
        h.Vm.SelectedBundle = bundle;
        Assert.True(h.Vm.ClearSelectionCommand.CanExecute(null));
        h.Vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, h.Vm.SelectedCount);
        Assert.Null(h.Vm.SelectedBundle);
        Assert.Empty(h.Vm.BundleNotices);
        Assert.False(h.Vm.HasBundleNotices);
    }


    [Fact]
    public void Header_select_all_ticks_installable_rows_then_clears()
    {
        // A not installed, B equal (not installable), C older, Foo blocked
        var h = Harness.Create(BundleCat(out _), Report(LicenseVerdict.CrackSuspected),
            x => x.Installed = p => p.Name == "B" ? "1.0" : p.Name == "C" ? "0.5" : null);
        Assert.False(h.Vm.AllInstallableSelected);
        h.Vm.AllInstallableSelected = true;
        Assert.Equal(new[] { "A", "C" }, h.Vm.Rows.Where(r => r.IsSelected).Select(r => r.Name));
        Assert.True(h.Vm.AllInstallableSelected);
        h.Vm.AllInstallableSelected = false;
        Assert.Equal(0, h.Vm.SelectedCount);
    }

    [Fact]
    public void Action_bar_shows_only_with_a_tick()
    {
        var h = Harness.Create(BundleCat(out _), Report(LicenseVerdict.CrackSuspected));
        Assert.False(h.Vm.HasSelection);
        h.Vm.Rows[0].IsSelected = true;
        Assert.True(h.Vm.HasSelection);
        Assert.Equal("Đã chọn 1 phần mềm", h.Vm.SelectedCountText);
        h.Vm.ClearSelectionCommand.Execute(null);
        Assert.False(h.Vm.HasSelection);
    }

    [Fact]
    public void Bundle_card_lists_items_and_installs_only_installable()
    {
        var h = Harness.Create(BundleCat(out _), Report(LicenseVerdict.CrackSuspected), x => x.Installed = p => p.Name == "A" ? "1.0" : null);
        var card = Assert.Single(h.Vm.BundleCards);
        Assert.Equal("Văn phòng", card.Name);
        Assert.Equal(new[] { "C", "Foo", "ghost", "A" }, card.Items.Select(i => i.Name));
        Assert.Null(card.Items[0].SkipReason);
        Assert.Equal("Không có trong kho", card.Items[2].SkipReason);
        Assert.Equal("đã cài trên máy", card.Items[3].SkipReason);
        Assert.Equal("4 phần mềm · 1 sẽ được cài · Bộ cho phòng kế toán", card.SummaryText);
        Assert.Equal("1.0", card.Items[0].Version);
        Assert.True(card.InstallCommand.CanExecute(null));
        card.InstallCommand.Execute(null);
        Assert.Equal(new[] { "C" }, h.Installs);
    }

    [Fact]
    public void Bundle_card_disabled_when_nothing_installable()
    {
        var h = Harness.Create(BundleCat(out _), Report(LicenseVerdict.CrackSuspected), x => x.Installed = p => p.Name == "A" || p.Name == "C" ? "1.0" : null);
        Assert.False(h.Vm.BundleCards[0].InstallCommand.CanExecute(null));
    }

    [Fact]
    public void Bundle_skips_newer_and_incomparable_with_notice()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected),
            x => x.Installed = p => p.Name == "A" ? "2.0" : p.Name == "C" ? "bản thử" : null);
        h.Vm.SelectedBundle = bundle;
        Assert.Equal(VersionStatus.Newer, h.Vm.Rows.Single(r => r.Name == "A").Status);
        Assert.Equal(VersionStatus.Incomparable, h.Vm.Rows.Single(r => r.Name == "C").Status);
        Assert.Equal(0, h.Vm.SelectedCount);
        Assert.Contains("A: đã cài trên máy", h.Vm.BundleNotices);
        Assert.Contains("C: đã cài trên máy", h.Vm.BundleNotices);
    }

    [Fact]
    public void After_ClearSelection_install_runs_in_display_order()
    {
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected));
        h.Vm.SelectedBundle = bundle; // bundle order C, A
        h.Vm.ClearSelectionCommand.Execute(null);
        foreach (var n in new[] { "C", "A" }) h.Vm.Rows.Single(r => r.Name == n).IsSelected = true;
        h.Vm.InstallSelectedCommand.Execute(null);
        Assert.Equal(new[] { "A", "C" }, h.Installs);
    }

    [Fact]
    public async Task ClearSelection_disabled_while_queue_runs_and_back_resets_bundle()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var h = Harness.Create(BundleCat(out var bundle), Report(LicenseVerdict.CrackSuspected), x => x.Install = (p, pr, ct) => gate.Task);
        h.Vm.SelectedBundle = bundle;
        h.Vm.InstallSelectedCommand.Execute(null);
        Assert.False(h.Vm.ClearSelectionCommand.CanExecute(null));
        gate.SetResult(Ok);
        var q = (QueueInstallViewModel)h.Nav[0];
        await q.Completion;
        q.BackCommand.Execute(null);
        Assert.True(h.Vm.ClearSelectionCommand.CanExecute(null));
        Assert.Null(h.Vm.SelectedBundle);
        Assert.False(h.Vm.HasBundleNotices);
    }

    [Fact]
    public void Bundle_picker_hidden_without_bundles()
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0")), Report());
        Assert.False(h.Vm.HasBundles);
        Assert.Empty(h.Vm.Bundles);
        Assert.False(h.Vm.HasBundleNotices);
    }

    [Fact]
    public async Task Install_disabled_while_queue_runs()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var h = Harness.Create(Cat(Pkg("A", "1.0"), Pkg("B", "1.0")), Report(), x =>
        {
            x.Install = (p, pr, ct) => gate.Task;
            x.Installed = _ => "0.5";
        });
        var (a, b) = (h.Vm.Rows[0], h.Vm.Rows[1]);
        b.IsSelected = true;
        Assert.True(b.InstallCommand.CanExecute(null));
        a.InstallCommand.Execute(null);
        Assert.True(h.Vm.IsInstalling);
        Assert.False(a.InstallCommand.CanExecute(null));
        Assert.False(b.InstallCommand.CanExecute(null));
        Assert.False(b.UninstallCommand.CanExecute(null));
        Assert.False(h.Vm.InstallSelectedCommand.CanExecute(null));
        gate.SetResult(Ok);
        var q = (QueueInstallViewModel)h.Nav[0];
        await q.Completion;
        q.BackCommand.Execute(null);
        Assert.False(h.Vm.IsInstalling);
        Assert.True(b.InstallCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(null, true)]     // not installed
    [InlineData("0.9", true)]    // older on the machine
    [InlineData("1.0", false)]   // same version
    [InlineData("2.0", false)]   // newer on the machine
    public void Install_button_greyed_when_machine_has_same_or_newer(string? installed, bool shown)
    {
        var h = Harness.Create(Cat(Pkg("A", "1.0")), Report(), x => x.Installed = _ => installed);
        Assert.Equal(shown, h.Vm.Rows[0].NeedsInstall);
        Assert.Equal(shown, h.Vm.Rows[0].InstallCommand.CanExecute(null));
    }
}
