using System.IO;
using Installer.App.ViewModels;
using Installer.Core;
using Installer.Core.Vault;

namespace Installer.App.Tests;

public class LoginViewModelTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_apptest_" + Guid.NewGuid().ToString("N"));
    readonly DateTimeOffset _now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public LoginViewModelTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    LoginViewModel Make(Func<VaultStore> open, Action<VaultSession>? ok = null) =>
        new(open, ok ?? (_ => { }), new FakeTime(_now));

    VaultStore NewVault() { VaultStore.Create(_tmp, "admin", "pw-1234"); return VaultStore.Open(_tmp); }

    [Fact]
    public async Task Login_success_invokes_onSuccess()
    {
        var store = NewVault();
        VaultSession? got = null;
        var vm = Make(() => store, s => got = s);
        vm.Username = "admin"; vm.Password = "pw-1234";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.NotNull(got);
        Assert.Equal("admin", got!.Username);
        Assert.Null(vm.Error);
        Assert.False(vm.IsBusy);
        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$", vm.VaultFingerprint!);
    }

    [Fact]
    public async Task Login_bad_password_sets_error_text()
    {
        var store = NewVault();
        var vm = Make(() => store);
        vm.Username = "admin"; vm.Password = "wrong";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal(Strings.BadCredentials, vm.Error);
    }

    [Fact]
    public async Task Login_locked_shows_until_time()
    {
        var store = NewVault();
        var vm = Make(() => store);
        vm.Username = "admin"; vm.Password = "wrong";
        for (int i = 0; i < 6; i++) { vm.Password = "wrong"; await vm.LoginCommand.ExecuteAsync(null); }
        var until = _now + TimeSpan.FromMinutes(15);
        Assert.Equal(string.Format(Strings.AccountLocked, until.ToLocalTime()), vm.Error);
    }

    [Fact]
    public async Task Login_missing_vault_shows_VaultNotFound()
    {
        var vm = Make(() => VaultStore.Open(Path.Combine(_tmp, "nope")));
        vm.Username = "a"; vm.Password = "b";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal(Strings.VaultNotFound, vm.Error);
    }

    [Fact]
    public async Task Fingerprint_visible_before_login_and_after_failed_login()
    {
        var store = NewVault();
        var vm = Make(() => store);
        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$", vm.VaultFingerprint!);
        Assert.StartsWith("Mã kho: ", vm.FingerprintText);
        vm.Username = "admin"; vm.Password = "wrong";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.NotNull(vm.VaultFingerprint);
    }

    [Fact]
    public async Task OnSuccess_throwing_sets_error_not_crash()
    {
        var store = NewVault();
        var vm = Make(() => store, _ => throw new InvalidOperationException("boom"));
        vm.Username = "admin"; vm.Password = "pw-1234";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal(Installer.Core.Strings.Unexpected, vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Password_cleared_after_attempt()
    {
        var store = NewVault();
        var vm = Make(() => store);
        vm.Username = "admin"; vm.Password = "wrong";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Password);
        vm.Password = "pw-1234";
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal("", vm.Password);
    }
}
