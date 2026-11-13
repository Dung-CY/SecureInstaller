using Installer.App.ViewModels;
using Installer.Core;
using Installer.Core.Model;

namespace Installer.App.Tests;

public class MainViewModelTests
{
    static Catalog Cat() => Catalog.Empty() with
    {
        Version = 7,
        UpdatedAt = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 11, 12, 0, 0)))
    };

    [Fact]
    public void VaultInfo_and_ReadOnlyNotice_text()
    {
        var vm = new MainViewModel();
        Assert.Null(vm.VaultInfo);
        vm.SetVault(Cat(), readOnly: true);
        Assert.Equal("Kho phiên bản 7 · cập nhật 11/11/2026", vm.VaultInfo);
        Assert.Equal(Strings.ReadOnlyNotice, vm.ReadOnlyNotice);
        vm.SetVault(Cat(), readOnly: false);
        Assert.Null(vm.ReadOnlyNotice);
    }

    [Fact]
    public void VaultInfo_is_culture_invariant()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var vm = new MainViewModel();
            vm.SetVault(Cat(), false);
            Assert.Equal("Kho phiên bản 7 · cập nhật 11/11/2026", vm.VaultInfo);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    static PackageEntry Pkg() => new("x", "x", "1.0", InstallerType.Zip, "a.zip", "", null, 60,
        new DetectRule(null, "x"), new(), new(), new(), "b.enc", "00", "AA==");

    [Fact]
    public async Task Close_blocked_while_queue_running()
    {
        var main = new MainViewModel();
        Assert.Null(main.CloseBlockedMessage);
        var tcs = new TaskCompletionSource<Installer.Core.Install.InstallOutcome>();
        var q = new QueueInstallViewModel("Cài đặt", new[] { Pkg(), Pkg() }, (_, _, _) => tcs.Task, () => { });
        main.NavigateTo(q);
        Assert.Equal("Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.", main.CloseBlockedMessage);
        tcs.SetResult(new(Installer.Core.Install.InstallStatus.Success, 0, "ok"));
        await q.Completion;
        Assert.Null(main.CloseBlockedMessage);

        var gate = new TaskCompletionSource<Installer.Core.Install.InstallOutcome>();
        var u = new QueueInstallViewModel("Gỡ cài đặt", new[] { Pkg() }, (_, _, _) => gate.Task, () => { }, "Đang gỡ");
        main.NavigateTo(u);
        Assert.Equal("Đang gỡ cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.", main.CloseBlockedMessage);
        gate.SetResult(new(Installer.Core.Install.InstallStatus.Success, 0, "ok"));
        await u.Completion;
        Assert.Null(main.CloseBlockedMessage);
    }
}
