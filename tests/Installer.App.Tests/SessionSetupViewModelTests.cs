using Installer.App.ViewModels;

namespace Installer.App.Tests;

public class SessionSetupViewModelTests
{
    readonly List<string> _renamed = new();
    SessionInfo? _done;

    SessionSetupViewModel Make(bool domain = false, string? pending = null, string? lastUnit = null, Action<string>? rename = null) =>
        new("DESKTOP-ABC", pending, domain, lastUnit, rename ?? _renamed.Add, s => _done = s);

    [Fact]
    public void Unit_is_required_and_normalized()
    {
        var vm = Make();
        vm.ContinueCommand.Execute(null);
        Assert.Equal("Nhập tên đơn vị", vm.Error);
        Assert.Null(_done);

        vm.Unit = "lâm thao";
        Assert.Equal("Sẽ lưu là: LamThao", vm.UnitPreview);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(new SessionInfo("LamThao", null), _done);
        Assert.Empty(_renamed);
    }

    [Fact]
    public void Rename_is_upper_cased_validated_and_applied()
    {
        var vm = Make(lastUnit: "HoaBinh");
        vm.WantRename = true;
        vm.NewName = "pth_01";
        vm.ContinueCommand.Execute(null);
        Assert.NotNull(vm.Error);
        Assert.Empty(_renamed);

        vm.NewName = "pth-lamthao-qlvbdh01";
        Assert.True(vm.NameTooLong);
        Assert.Equal("20/15 ký tự", vm.NameLengthText);
        vm.ContinueCommand.Execute(null);   // too long for NetBIOS is a warning, not an error
        Assert.Equal(new[] { "PTH-LAMTHAO-QLVBDH01" }, _renamed);
        Assert.Equal(new SessionInfo("HoaBinh", "PTH-LAMTHAO-QLVBDH01"), _done);
    }

    [Fact]
    public void Rename_failure_stays_on_the_screen()
    {
        var vm = Make(lastUnit: "HoaBinh", rename: _ => throw new UnauthorizedAccessException());
        vm.WantRename = true;
        vm.NewName = "PTH-01";
        vm.ContinueCommand.Execute(null);
        Assert.StartsWith("Không đổi được tên máy", vm.Error);
        Assert.Null(_done);
    }

    [Fact]
    public void Domain_joined_machine_cannot_rename()
    {
        var vm = Make(domain: true, pending: "OLD-PENDING", lastUnit: "HoaBinh");
        Assert.False(vm.CanRename);
        vm.WantRename = true;
        vm.NewName = "PTH-01";
        vm.ContinueCommand.Execute(null);
        Assert.Empty(_renamed);
        Assert.Equal(new SessionInfo("HoaBinh", "OLD-PENDING"), _done);
    }
}
