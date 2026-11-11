using Installer.App.ViewModels;
using Installer.Core.Errors;
using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.App.Tests;

public class QueueInstallViewModelTests
{
    internal sealed class SyncProgress : IProgress<InstallProgress>
    {
        readonly Action<InstallProgress> _a;
        public SyncProgress(Action<InstallProgress> a) => _a = a;
        public void Report(InstallProgress v) => _a(v);
    }

    static PackageEntry Pkg(string name) =>
        new("id-" + name, name, "1.0", InstallerType.Zip, "a.zip", "", null, 60,
            new DetectRule(null, "^" + name + "$"), new(), new(), new(), "b.enc", "00", "AA==");

    static InstallOutcome Out(InstallStatus s, string msg = "m") => new(s, 0, msg);

    static QueueInstallViewModel Make(IReadOnlyList<PackageEntry> items,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run,
        Action? back = null, string runningText = "Đang cài") =>
        new("Cài đặt", items, run, back ?? (() => { }), a => new SyncProgress(a), runningText);

    [Fact]
    public async Task Install_selected_runs_in_order_and_reports_each_status()
    {
        var order = new List<string>();
        var a = Pkg("A"); var b = Pkg("B"); var c = Pkg("C");
        var vm = Make(new[] { a, b, c }, (p, pr, ct) =>
        {
            order.Add(p.Name);
            return Task.FromResult(p.Name switch
            {
                "A" => Out(InstallStatus.Success),
                "B" => Out(InstallStatus.RebootRequired),
                _ => Out(InstallStatus.Timeout, "Quá thời gian"),
            });
        });
        await vm.Completion;
        Assert.Equal(new[] { "A", "B", "C" }, order);
        Assert.Equal(new[] { "Thành công", "Cần khởi động lại", "Lỗi" }, vm.Items.Select(i => i.Status));
        Assert.Equal("Quá thời gian", vm.Items[2].Message);
        Assert.True(vm.IsDone);
        Assert.Contains(QueueInstallViewModel.RebootLine, vm.Result);
        Assert.True(vm.BackCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Header_progress_and_running_text_track_current_item()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var vm = Make(new[] { Pkg("A"), Pkg("B") }, (p, pr, ct) =>
        {
            if (p.Name == "A") return Task.FromResult(Out(InstallStatus.Success));
            pr.Report(new InstallProgress(40, "Đang gỡ bỏ"));
            return gate.Task;
        }, runningText: "Đang gỡ");
        Assert.Equal("Gói 2/2", vm.Header);
        Assert.Equal("Thành công", vm.Items[0].Status);
        Assert.Equal("Đang gỡ", vm.Items[1].Status);
        Assert.Equal(40, vm.Percent);
        Assert.False(vm.IsIndeterminate);
        Assert.Equal("Đang gỡ bỏ", vm.Step);
        Assert.False(vm.IsDone);
        Assert.False(vm.BackCommand.CanExecute(null));
        gate.SetResult(Out(InstallStatus.Success));
        await vm.Completion;
        Assert.DoesNotContain(QueueInstallViewModel.RebootLine, vm.Result);
    }

    [Fact]
    public async Task Failed_item_does_not_stop_queue()
    {
        var ran = new List<string>();
        var vm = Make(new[] { Pkg("A"), Pkg("B"), Pkg("C") }, (p, pr, ct) =>
        {
            ran.Add(p.Name);
            if (p.Name == "A") return Task.FromResult(new InstallOutcome(InstallStatus.Failed, 1603, "Cài đặt thất bại"));
            if (p.Name == "B") throw new InvalidOperationException("boom");
            return Task.FromResult(Out(InstallStatus.Success));
        });
        await vm.Completion;
        Assert.Equal(new[] { "A", "B", "C" }, ran);
        Assert.Equal(new[] { "Lỗi", "Lỗi", "Thành công" }, vm.Items.Select(i => i.Status));
        Assert.Equal("Cài đặt thất bại", vm.Items[0].Message);
        Assert.Equal(Installer.Core.Strings.Unexpected, vm.Items[1].Message);
    }

    [Fact]
    public async Task Cancel_marks_current_cancelled_and_rest_skipped()
    {
        var started = new TaskCompletionSource();
        var ran = new List<string>();
        var vm = Make(new[] { Pkg("A"), Pkg("B"), Pkg("C"), Pkg("D") }, async (p, pr, ct) =>
        {
            ran.Add(p.Name);
            if (p.Name == "A") return Out(InstallStatus.Success);
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { return new InstallOutcome(InstallStatus.Failed, null, "Người dùng hủy cài đặt"); }
            return Out(InstallStatus.Success);
        });
        await started.Task;
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await vm.Completion;
        Assert.Equal(new[] { "A", "B" }, ran);
        Assert.Equal(new[] { "Thành công", "Đã hủy", "Đã bỏ qua", "Đã bỏ qua" }, vm.Items.Select(i => i.Status));
        Assert.True(vm.IsDone);
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_via_thrown_exception_also_marks_cancelled()
    {
        var started = new TaskCompletionSource();
        var vm = Make(new[] { Pkg("A"), Pkg("B") }, async (p, pr, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Out(InstallStatus.Success);
        });
        await started.Task;
        vm.CancelCommand.Execute(null);
        await vm.Completion;
        Assert.Equal(new[] { "Đã hủy", "Đã bỏ qua" }, vm.Items.Select(i => i.Status));
    }

    [Fact]
    public async Task Cancel_shows_dang_huy_immediately()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        IProgress<InstallProgress>? progress = null;
        var vm = Make(new[] { Pkg("A"), Pkg("B") }, (p, pr, ct) => { progress = pr; return gate.Task; });
        vm.CancelCommand.Execute(null);
        Assert.Equal("Đang hủy…", vm.Step);
        progress!.Report(new InstallProgress(50, "Đang chép tệp")); // late progress must not hide the feedback
        Assert.Equal("Đang hủy…", vm.Step);
        gate.SetResult(Out(InstallStatus.Failed, ErrorText.Cancelled));
        await vm.Completion;
        Assert.Equal(new[] { "Đã hủy", "Đã bỏ qua" }, vm.Items.Select(i => i.Status));
        Assert.False(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null); // after the run (token source disposed) this must be a no-op
    }

    [Fact]
    public async Task Cancellation_not_from_user_is_an_error()
    {
        var ran = new List<string>();
        var vm = Make(new[] { Pkg("A"), Pkg("B") }, (p, pr, ct) =>
        {
            ran.Add(p.Name);
            if (p.Name == "A") throw new TaskCanceledException("timeout");
            return Task.FromResult(Out(InstallStatus.Success));
        });
        await vm.Completion;
        Assert.Equal(new[] { "A", "B" }, ran);
        Assert.Equal(new[] { "Lỗi", "Thành công" }, vm.Items.Select(i => i.Status));
        Assert.Equal(ErrorText.ForUser(new TaskCanceledException()), vm.Items[0].Message);
        Assert.Equal("Hoàn tất", vm.Step);
    }

    [Fact]
    public async Task Empty_queue_is_done_immediately()
    {
        var vm = Make(Array.Empty<PackageEntry>(), (p, pr, ct) => Task.FromResult(Out(InstallStatus.Success)));
        await vm.Completion;
        Assert.True(vm.IsDone);
        Assert.Empty(vm.Items);
        Assert.True(vm.BackCommand.CanExecute(null));
    }

    [Fact]
    public void Waiting_items_and_indeterminate_initially()
    {
        var gate = new TaskCompletionSource<InstallOutcome>();
        var vm = Make(new[] { Pkg("A"), Pkg("B") }, (p, pr, ct) => gate.Task);
        Assert.True(vm.IsIndeterminate);
        Assert.Equal("Gói 1/2", vm.Header);
        Assert.Equal(new[] { "Đang cài", "Đang chờ" }, vm.Items.Select(i => i.Status));
        gate.SetResult(Out(InstallStatus.Success));
    }

    [Fact]
    public async Task Back_invokes_callback_after_done()
    {
        var backs = 0;
        var vm = Make(new[] { Pkg("A") }, (p, pr, ct) => Task.FromResult(Out(InstallStatus.Success)), () => backs++);
        await vm.Completion;
        vm.BackCommand.Execute(null);
        Assert.Equal(1, backs);
    }
}
