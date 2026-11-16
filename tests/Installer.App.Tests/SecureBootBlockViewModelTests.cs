using Installer.App.ViewModels;

namespace Installer.App.Tests;

public class SecureBootBlockViewModelTests
{
    [Fact]
    public void Recheck_clears_when_secure_boot_is_now_off()
    {
        var cleared = false;
        var vm = new SecureBootBlockViewModel("msg", "guide", () => false, () => { }, () => { }, () => cleared = true);
        vm.RecheckCommand.Execute(null);
        Assert.True(cleared);
        Assert.Null(vm.Status);
    }

    [Fact]
    public void Recheck_stays_blocked_while_secure_boot_is_still_on()
    {
        var cleared = false;
        var vm = new SecureBootBlockViewModel("msg", "guide", () => true, () => { }, () => { }, () => cleared = true);
        vm.RecheckCommand.Execute(null);
        Assert.False(cleared);
        Assert.Contains("vẫn đang bật", vm.Status);
    }

    [Fact]
    public void Restart_and_exit_fire_their_actions()
    {
        int restart = 0, exit = 0;
        var vm = new SecureBootBlockViewModel("msg", "guide", () => true, () => restart++, () => exit++, () => { });
        vm.RestartCommand.Execute(null);
        vm.ExitCommand.Execute(null);
        Assert.Equal((1, 1), (restart, exit));
    }
}
