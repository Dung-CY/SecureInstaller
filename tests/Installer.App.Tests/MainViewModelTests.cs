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
}
