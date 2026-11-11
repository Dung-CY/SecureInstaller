using Installer.App.ViewModels;
using Installer.Core.Install;

namespace Installer.App.Tests;

public class InstallViewModelTests
{
    sealed class SyncProgress : IProgress<InstallProgress>
    {
        readonly Action<InstallProgress> _a;
        public SyncProgress(Action<InstallProgress> a) => _a = a;
        public void Report(InstallProgress v) => _a(v);
    }

    [Fact]
    public async Task Cancel_during_install_reports_cancel_message()
    {
        var started = new TaskCompletionSource();
        var vm = new InstallViewModel("Foo 1.0", async (pr, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException)
            {
                return new InstallOutcome(InstallStatus.Failed, null, "Người dùng hủy cài đặt");
            }
            return new InstallOutcome(InstallStatus.Success, 0, "ok");
        }, () => { }, a => new SyncProgress(a));

        await started.Task;
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await vm.Completion;
        Assert.Equal("Người dùng hủy cài đặt", vm.Result);
        Assert.False(vm.IsSuccess);
        Assert.True(vm.IsDone);
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Run_exception_becomes_failed_result()
    {
        var vm = new InstallViewModel("Foo", (pr, ct) => throw new InvalidOperationException("boom"), () => { },
            a => new SyncProgress(a));
        await vm.Completion;
        Assert.Equal(Installer.Core.Strings.Unexpected, vm.Result);
        Assert.True(vm.IsDone);
        Assert.False(vm.IsSuccess);
    }

    [Fact]
    public void Indeterminate_when_percent_null()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var vm = new InstallViewModel("Foo", (pr, ct) => gate.Task, () => { }, a => new SyncProgress(a));
        Assert.True(vm.IsIndeterminate);
        gate.SetResult(new(InstallStatus.Success, 0, "ok"));
    }
}
